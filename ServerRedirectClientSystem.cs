using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
            ServerRedirectLang.Get("hotkey-open"),
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
            _api?.ShowChatMessage(ServerRedirectLang.Get("chat-unavailable"));
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
            _api?.ShowChatMessage(ServerRedirectLang.Get("chat-unavailable"));
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

        string host = packet.Host?.Trim() ?? string.Empty;
        string name = string.IsNullOrWhiteSpace(packet.Name) ? host : packet.Name.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            _api?.ShowChatMessage(ServerRedirectLang.Get("chat-emptytarget"));
            return;
        }

        _redirectInProgress = true;
        _dialog?.TryClose();

        try
        {
            ICoreClientAPI api = _api ?? throw new InvalidOperationException(ServerRedirectLang.Get("error-clientapi-unavailable"));
            _api?.ShowChatMessage(ServerRedirectLang.Get("chat-redirecting", name, host));
            api.Event.EnqueueMainThreadTask(
                () => TrySwitchServerInCurrentClientSafely(host, name),
                "serverredirect-switch");
        }
        catch (Exception ex)
        {
            _redirectInProgress = false;
            _api?.ShowChatMessage(ServerRedirectLang.Get("chat-failed", ex.Message));
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
            _api?.ShowChatMessage(ServerRedirectLang.Get("chat-failed", ex.GetBaseException().Message));
        }
    }

    private void TrySwitchServerInCurrentClient(string host, string name)
    {
        object? clientMain = _api?.World;
        if (clientMain is null)
        {
            throw new InvalidOperationException(ServerRedirectLang.Get("error-clientapi-unavailable"));
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
            throw new InvalidOperationException(ServerRedirectLang.Get("error-clientmethods-missing"));
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
