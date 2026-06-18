using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ServerRedirect;

public sealed class ServerRedirectServerSystem : ModSystem
{
    private const string Command = "serverredirect";
    private const string ConfigFileName = "serverredirect.json";
    private const string Privilege = "controlserver";
    private const string ChannelName = "serverredirect";

    private ICoreServerAPI? _api;
    private IServerNetworkChannel? _channel;
    private RedirectConfig _config = new();

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Server;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        _config = LoadConfig(api);

        _channel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<ServerRedirectListRequest>()
            .RegisterMessageType<ServerRedirectSelectRequest>()
            .RegisterMessageType<ServerRedirectExecutePacket>()
            .RegisterMessageType<ServerRedirectListPacket>()
            .SetMessageHandler<ServerRedirectListRequest>(OnListRequest)
            .SetMessageHandler<ServerRedirectSelectRequest>(OnSelectRequest);

        RegisterCommands(api);
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        CommandArgumentParsers parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create(Command)
            .WithDescription("Manage the server redirect list")
            .RequiresPrivilege(Privilege)
            .BeginSubCommand("add")
                .WithDescription("Add or replace a redirect target")
                .WithArgs(parsers.Word("host:port"), parsers.All("name"))
                .HandleWith(AddRedirect)
            .EndSubCommand()
            .BeginSubCommand("del")
                .WithDescription("Delete a redirect target by name, or by host:port and name")
                .WithArgs(parsers.All("name or host:port name"))
                .HandleWith(DeleteRedirect)
            .EndSubCommand()
            .BeginSubCommand("list")
                .WithDescription("List all redirect targets")
                .HandleWith(ListRedirects);
    }

    private TextCommandResult AddRedirect(TextCommandCallingArgs args)
    {
        string host = (string)args[0];
        string name = (string)args[1];

        if (!NormalizeHost(ref host, out var error))
        {
            return TextCommandResult.Error(error);
        }

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return TextCommandResult.Error("Missing redirect name.");
        }

        int existingIndex = Array.FindIndex(_config.Entries, entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        var entry = new ServerRedirectEntry { Host = host, Name = name };
        if (existingIndex >= 0)
        {
            _config.Entries[existingIndex] = entry;
        }
        else
        {
            _config.Entries = _config.Entries.Append(entry).ToArray();
        }

        SortEntries();
        SaveConfig();
        BroadcastList();
        return TextCommandResult.Success($"Redirect '{name}' -> {host} saved.");
    }

    private TextCommandResult DeleteRedirect(TextCommandCallingArgs args)
    {
        string input = ((string)args[0]).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return TextCommandResult.Error("Missing redirect name.");
        }

        ServerRedirectEntry? entry = FindEntryForDelete(input);
        if (entry is null)
        {
            return TextCommandResult.Error($"No redirect target matching '{input}' exists.");
        }

        _config.Entries = _config.Entries
            .Where(item => !string.Equals(item.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        SaveConfig();
        BroadcastList();
        return TextCommandResult.Success($"Redirect '{entry.Name}' -> {entry.Host} deleted.");
    }

    private TextCommandResult ListRedirects(TextCommandCallingArgs args)
    {
        if (_config.Entries.Length == 0)
        {
            return TextCommandResult.Success("No redirect targets configured.");
        }

        string lines = string.Join("\n", _config.Entries.Select(entry => $"{entry.Name} -> {entry.Host}"));
        return TextCommandResult.Success(lines);
    }

    private void OnListRequest(IServerPlayer fromPlayer, ServerRedirectListRequest message)
    {
        SendList(fromPlayer);
    }

    private void OnSelectRequest(IServerPlayer fromPlayer, ServerRedirectSelectRequest message)
    {
        string name = message.Name.Trim();
        ServerRedirectEntry? entry = _config.Entries.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, $"No redirect named '{name}' exists.", EnumChatType.CommandError);
            SendList(fromPlayer);
            return;
        }

        if (TryRedirect(fromPlayer, entry.Host, entry.Name, out var error))
        {
            _api?.Logger.Notification("Redirecting {0} to {1} ({2})", fromPlayer.PlayerName, entry.Host, entry.Name);
            return;
        }

        fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, $"Redirect failed: {error}", EnumChatType.CommandError);
    }

    private void SendList(IServerPlayer player)
    {
        _channel?.SendPacket(new ServerRedirectListPacket { Entries = CloneEntries() }, player);
    }

    private void BroadcastList()
    {
        IServerPlayer[] players = _api?.World.AllOnlinePlayers.OfType<IServerPlayer>().ToArray() ?? [];
        if (players.Length == 0)
        {
            return;
        }

        _channel?.SendPacket(new ServerRedirectListPacket { Entries = CloneEntries() }, players);
    }

    private ServerRedirectEntry[] CloneEntries()
    {
        return _config.Entries
            .Select(entry => new ServerRedirectEntry { Host = entry.Host, Name = entry.Name })
            .ToArray();
    }

    private bool TryRedirect(IServerPlayer player, string host, string name, out string error)
    {
        if (_channel is null)
        {
            error = "Network channel is not ready.";
            return false;
        }

        try
        {
            _channel.SendPacket(
                new ServerRedirectExecutePacket
                {
                    Host = host,
                    Name = name
                },
                player);

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private RedirectConfig LoadConfig(ICoreServerAPI api)
    {
        try
        {
            RedirectConfig? config = api.LoadModConfig<RedirectConfig>(ConfigFileName);
            if (config is not null)
            {
                config.Entries = config.Entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Host) && !string.IsNullOrWhiteSpace(entry.Name))
                    .Select(entry => new ServerRedirectEntry { Host = entry.Host.Trim(), Name = entry.Name.Trim() })
                    .ToArray();
                _config = config;
                SortEntries();
                return _config;
            }
        }
        catch (Exception ex)
        {
            api.Logger.Warning("Server Redirect failed loading {0}: {1}", ConfigFileName, ex.Message);
        }

        var defaults = new RedirectConfig();
        _config = defaults;
        SaveConfig();
        return defaults;
    }

    private void SaveConfig()
    {
        if (_api is null)
        {
            return;
        }

        try
        {
            _api.StoreModConfig(_config, ConfigFileName);
        }
        catch (Exception ex)
        {
            _api.Logger.Warning("Server Redirect failed saving {0}: {1}", ConfigFileName, ex.Message);
        }
    }

    private void SortEntries()
    {
        _config.Entries = _config.Entries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ServerRedirectEntry? FindEntryForDelete(string input)
    {
        ServerRedirectEntry? byName = _config.Entries
            .FirstOrDefault(entry => string.Equals(entry.Name, input, StringComparison.OrdinalIgnoreCase));

        if (byName is not null)
        {
            return byName;
        }

        int separator = input.IndexOf(' ');
        if (separator < 0)
        {
            return null;
        }

        string host = input[..separator];
        string name = input[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(name) || !NormalizeHost(ref host, out _))
        {
            return null;
        }

        return _config.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Host, host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NormalizeHost(ref string host, out string error)
    {
        host = host.Trim();
        if (host.StartsWith("vintagestoryjoin://", StringComparison.OrdinalIgnoreCase))
        {
            host = host["vintagestoryjoin://".Length..];
        }

        host = host.TrimEnd('/');
        if (host.Length == 0 || host.Any(char.IsWhiteSpace))
        {
            error = "Invalid host. Use host:port.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed class RedirectConfig
    {
        public ServerRedirectEntry[] Entries { get; set; } = [];
    }
}
