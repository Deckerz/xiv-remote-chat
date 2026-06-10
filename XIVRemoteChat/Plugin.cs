using System;

using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using XIVRemoteChat.Services;
using XIVRemoteChat.Windows;

namespace XIVRemoteChat;

public sealed class Plugin : IDalamudPlugin
{
    internal const string ServerBaseUrl = "https://xivremote.chat";
    internal const string WebSocketBaseUrl = "wss://xivremote.chat";
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("XIV Remote Chat##XIVRemoteChat");
    private readonly ConfigWindow configWindow;

    public Configuration Configuration { get; init; }
    internal TokenRenewalService TokenRenewalService { get; private set; } = null!;
    internal ChatListenerService ChatListenerService { get; private set; }
    internal SendListenerService SendListenerService { get; private set; }
    internal FriendListRefreshService FriendListRefreshService { get; private set; } = null!;
    private static readonly string ALL_CHARACTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";


    public Plugin()
    {
        var pluginConfig = PluginInterface.GetPluginConfig();

        Configuration = pluginConfig as Configuration ?? new Configuration()
        {
            EncryptionPassword = Random.Shared.GetString(ALL_CHARACTERS, 8),
            EncryptionSalt = Random.Shared.GetString(ALL_CHARACTERS, 8),
        };

        if (pluginConfig == null)
        {
            PluginInterface.SavePluginConfig(Configuration);
        }

        TokenRenewalService = new TokenRenewalService(Configuration);
        ChatListenerService = new ChatListenerService(this);
        SendListenerService = new SendListenerService(Configuration);
        FriendListRefreshService = new FriendListRefreshService(Configuration);

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUI;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUI;

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        ChatListenerService.Dispose();
        SendListenerService.Dispose();
        TokenRenewalService.Dispose();
        FriendListRefreshService.Dispose();
    }

    private void DrawUI() => windowSystem.Draw();
    private void OpenConfigUI() => configWindow.Toggle();
}
