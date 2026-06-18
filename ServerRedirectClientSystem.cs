using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ServerRedirect;

public sealed class ServerRedirectClientSystem : ModSystem
{
    private const string ChannelName = "serverredirect";
    private const string HotkeyCode = "serverredirect-window";

    private ICoreClientAPI? _api;
    private IClientNetworkChannel? _channel;
    private ServerRedirectDialog? _dialog;
    private bool _redirectInProgress;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _api = api;
        _channel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<ServerRedirectListRequest>()
            .RegisterMessageType<ServerRedirectSelectRequest>()
            .RegisterMessageType<ServerRedirectExecutePacket>()
            .RegisterMessageType<ServerRedirectListPacket>()
            .SetMessageHandler<ServerRedirectExecutePacket>(OnExecutePacket)
            .SetMessageHandler<ServerRedirectListPacket>(OnListPacket);

        _dialog = new ServerRedirectDialog(api, RequestRedirect, RequestList);
        api.Gui.RegisterDialog(_dialog);

        api.Input.RegisterHotKey(
            HotkeyCode,
            "Open Server Redirect",
            GlKeys.R,
            HotkeyType.GUIOrOtherControls,
            altPressed: true);
        api.Input.SetHotKeyHandler(HotkeyCode, OnHotkey);
    }

    public override void Dispose()
    {
        _dialog?.TryClose();
        _dialog?.Dispose();
        _dialog = null;
        _api = null;
        _channel = null;
        base.Dispose();
    }

    private bool OnHotkey(KeyCombination keyCombination)
    {
        if (_dialog is null)
        {
            return false;
        }

        if (_dialog.IsOpened())
        {
            _dialog.TryClose();
        }
        else
        {
            _dialog.TryOpen();
        }

        return true;
    }

    private void RequestList()
    {
        if (_channel?.Connected == true)
        {
            _channel.SendPacket(new ServerRedirectListRequest());
        }
        else
        {
            _api?.ShowChatMessage("Server Redirect is not available on this server.");
        }
    }

    private void RequestRedirect(string name)
    {
        if (_channel?.Connected == true)
        {
            _channel.SendPacket(new ServerRedirectSelectRequest { Name = name });
        }
        else
        {
            _api?.ShowChatMessage("Server Redirect is not available on this server.");
        }
    }

    private void OnListPacket(ServerRedirectListPacket packet)
    {
        _dialog?.SetEntries(packet.Entries ?? []);
    }

    private void OnExecutePacket(ServerRedirectExecutePacket packet)
    {
        if (_redirectInProgress)
        {
            return;
        }

        string host = packet.Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            _api?.ShowChatMessage("Server Redirect received an empty target.");
            return;
        }

        _redirectInProgress = true;
        _dialog?.TryClose();

        try
        {
            ICoreClientAPI api = _api ?? throw new InvalidOperationException("Client API is not available.");
            _api?.ShowChatMessage($"Redirecting to {packet.Name} ({host})...");
            api.Event.EnqueueMainThreadTask(
                () => TrySwitchServerInCurrentClientSafely(host, packet.Name),
                "serverredirect-switch");
        }
        catch (Exception ex)
        {
            _redirectInProgress = false;
            _api?.ShowChatMessage($"Server Redirect failed: {ex.Message}");
        }
    }

    private void TrySwitchServerInCurrentClientSafely(string host, string name)
    {
        try
        {
            TrySwitchServerInCurrentClient(host, name);
        }
        catch (Exception ex)
        {
            _redirectInProgress = false;
            _api?.ShowChatMessage($"Server Redirect failed: {ex.GetBaseException().Message}");
        }
    }

    private void TrySwitchServerInCurrentClient(string host, string name)
    {
        object? clientMain = _api?.World;
        if (clientMain is null)
        {
            throw new InvalidOperationException("Could not access the current Vintage Story client.");
        }

        Type clientType = clientMain.GetType();
        MethodInfo? sendLeave = clientType.GetMethod("SendLeave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? destroySession = clientType.GetMethod(
            "DestroyGameSession",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(bool), typeof(EnumExitMode)],
            modifiers: null);
        FieldInfo? redirectField = clientType.GetField("RedirectTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo? exitReasonField = clientType.GetField("exitReason", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo? exitToMainMenuField = clientType.GetField("exitToMainMenu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo? exitToDisconnectScreenField = clientType.GetField("exitToDisconnectScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (sendLeave is null || destroySession is null || redirectField is null)
        {
            throw new InvalidOperationException("Could not find Vintage Story's client session methods.");
        }

        var entry = new MultiplayerServerEntry
        {
            host = host,
            name = name
        };

        try
        {
            sendLeave.Invoke(clientMain, [0]);
        }
        catch
        {
            // The server may already be closing the channel. Continue with local teardown.
        }

        exitReasonField?.SetValue(clientMain, "server redirect");
        exitToMainMenuField?.SetValue(clientMain, false);
        exitToDisconnectScreenField?.SetValue(clientMain, false);
        destroySession.Invoke(clientMain, [false, EnumExitMode.SoftExit]);
        redirectField.SetValue(clientMain, entry);
    }
}
