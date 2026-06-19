using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;

using FFXIVClientStructs.FFXIV.Client.Game.UI;

using XIVRemoteChat.Services;

namespace XIVRemoteChat.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private readonly AuthCallbackServer authCallbackServer;

    private string statusMessage = string.Empty;
    private bool statusIsError;

    private const string LoginUrl = $"{Plugin.ServerBaseUrl}/auth/login/discord";

    private static readonly XivChatType[] LinkshellTypes =
    [
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
    ];

    private static readonly XivChatType[] CrossLinkshellTypes =
    [
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2,
        XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    public ConfigWindow(Plugin plugin) : base("XIV Remote Chat Configuration###XIVRemoteChatConfig")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 350),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
        configuration = plugin.Configuration;

        authCallbackServer = new AuthCallbackServer(configuration, OnAuthTokenReceived);
    }

    public void Dispose() => authCallbackServer.Dispose();

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##channels", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
        {
            return;
        }

        if (ImGui.BeginTabItem("Info"))
        {
            ImGui.TextUnformatted("XIV Remote Chat");
            ImGui.TextWrapped("Is a work in progess plugin that allows you to forward your in-game chat to a server, and receive messages from that server to send in-game. Good for people who need to go away from their computer but still want to chat. Discord account is the only thing needed to log in and set this up.");
            ImGui.TextWrapped("Messages are end-to-end encrypted, so only you can read them. If you want you can change the encryption password and salt in the settings, but keep in mind that doing so will make old messages unreadable. They are stored encrypted on the server for 1 day before deleting, allowing you to continue your conversations on the go without having to remember them.");
            ImGui.EndTabItem();
        }

        if (!string.IsNullOrEmpty(configuration.Token) && ImGui.BeginTabItem("Status"))
        {
            DrawStatusTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Account"))
        {
            DrawAccountTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("General"))
        {
            DrawChannelToggle("Say", XivChatType.Say);
            DrawChannelToggle("Shout", XivChatType.Shout);
            DrawChannelToggle("Yell", XivChatType.Yell);
            DrawChannelToggle("Echo", XivChatType.Echo);
            ImGui.Separator();
            DrawChannelToggle("Tell (Incoming)", XivChatType.TellIncoming);
            DrawChannelToggle("Tell (Outgoing)", XivChatType.TellOutgoing);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Social"))
        {
            DrawChannelToggle("Party", XivChatType.Party);
            DrawChannelToggle("Alliance", XivChatType.Alliance);
            DrawChannelToggle("Free Company", XivChatType.FreeCompany);
            DrawChannelToggle("PvP Team", XivChatType.PvPTeam);
            DrawChannelToggle("Novice Network", XivChatType.NoviceNetwork);
            ImGui.Separator();
            DrawChannelToggle("Custom Emote", XivChatType.CustomEmote);
            DrawChannelToggle("Standard Emote", XivChatType.StandardEmote);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Linkshells"))
        {
            for (var i = 0; i < LinkshellTypes.Length; i++)
            {
                DrawChannelToggle($"Linkshell {i + 1}", LinkshellTypes[i]);
            }

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Cross-world"))
        {
            for (var i = 0; i < CrossLinkshellTypes.Length; i++)
            {
                DrawChannelToggle($"Cross-world Linkshell {i + 1}", CrossLinkshellTypes[i]);
            }


            ImGui.EndTabItem();
        }

#if DEBUG
        if (ImGui.BeginTabItem("Debug"))
        {
            DrawDebugTab();
            ImGui.EndTabItem();
        }
#endif
        ImGui.EndTabBar();
    }

    private void DrawAccountTab()
    {
        var isLoggedIn = !string.IsNullOrEmpty(configuration.Token);

        ImGui.TextUnformatted("Status:");
        ImGui.SameLine();
        if (isLoggedIn)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.85f, 0.2f, 1f), "Logged in");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.85f, 0.2f, 0.2f, 1f), "Not logged in");
        }

        ImGui.Spacing();

        if (ImGui.Button("Open Login Page"))
        {
            try
            {
                authCallbackServer.Start();
                Dalamud.Utility.Util.OpenLink(LoginUrl);
            }
            catch (Exception ex)
            {
                SetPasteStatus($"Could not start login listener: {ex.Message}", isError: true);
                Plugin.Log.Error(ex, "Could not start login listener");
            }
        }

        if (authCallbackServer.IsRunning)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.9f, 0.75f, 0.1f, 1f), "Waiting for login callback...");
        }

        if (isLoggedIn)
        {
            bool iHaveALargeFriendsList = configuration.IHaveALargeFriendsList;
			if (ImGui.Checkbox("I have a large friends list (100+)", ref iHaveALargeFriendsList))
			{
				configuration.IHaveALargeFriendsList = iHaveALargeFriendsList;
				configuration.Save();
			}
            
            ImGui.Spacing();
            if (ImGui.Button("Force Friends List Refresh"))
            {
                plugin.FriendListRefreshService.SendNow();
            }
            ImGui.SameLine();
            if (ImGui.Button("Log Out"))
            {
                configuration.Token = null;
                configuration.Save();
                plugin.TokenRenewalService.ScheduleRenewal();
                statusMessage = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Encryption Settings (Both are needed for it to work):");
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.78f, 1f));
        ImGui.TextWrapped("These are used to encrypt messages in the remote client, so that only you can read them. You can change them at any time, but changing them will make old messages unreadable.");
        ImGui.PopStyleColor();
        var currentEncryptionPassword = configuration.EncryptionPassword ?? string.Empty;
        if (ImGui.InputText("Encryption Password", ref currentEncryptionPassword, 100))
        {
            configuration.EncryptionPassword = currentEncryptionPassword;
            configuration.Save();
        }

        var currentEncryptionSalt = configuration.EncryptionSalt ?? string.Empty;
        if (ImGui.InputText("Encryption Salt", ref currentEncryptionSalt, 100))
        {
            configuration.EncryptionSalt = currentEncryptionSalt;
            configuration.Save();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.Spacing();
            var color = statusIsError
                ? new Vector4(0.85f, 0.2f, 0.2f, 1f)
                : new Vector4(0.2f, 0.85f, 0.2f, 1f);
            ImGui.TextColored(color, statusMessage);
        }
    }

    private void DrawStatusTab()
    {
        DrawSocketStatus("Chat Listener", "Forwards in-game chat to the server.",
                         plugin.ChatListenerService.State, plugin.ChatListenerService.Connect);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawSocketStatus("Send Listener", "Receives messages from the server to send in-game.",
                         plugin.SendListenerService.State, plugin.SendListenerService.Connect);
    }

    private static void DrawSocketStatus(string label, string description, WebSocketState? state, Action reconnect)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();

        var (text, color) = state switch
        {
            WebSocketState.Open => ("Connected", new Vector4(0.2f, 0.85f, 0.2f, 1f)),
            WebSocketState.Connecting => ("Connecting...", new Vector4(0.9f, 0.75f, 0.1f, 1f)),
            WebSocketState.CloseSent or WebSocketState.CloseReceived => ("Closing...", new Vector4(0.9f, 0.75f, 0.1f, 1f)),
            null or WebSocketState.None or WebSocketState.Closed or WebSocketState.Aborted
                => ("Disconnected", new Vector4(0.85f, 0.2f, 0.2f, 1f)),
            _ => (state.ToString(), new Vector4(0.85f, 0.2f, 0.2f, 1f)),
        };
        ImGui.TextColored(color, text);

        ImGui.TextDisabled(description);

        if (state != WebSocketState.Open)
        {
            if (ImGui.Button($"Reconnect##{label}"))
            {
                reconnect();
            }
        }
    }

    private void OnAuthTokenReceived()
    {
        plugin.TokenRenewalService.ScheduleRenewal();

        if (!string.IsNullOrEmpty(Plugin.PlayerState.CharacterName))
        {
            plugin.ChatListenerService.Connect();
            plugin.SendListenerService.Connect();
        }

        SetPasteStatus("Token saved successfully.", isError: false);
        authCallbackServer.Stop();
    }

    private void SetPasteStatus(string message, bool isError)
    {
        statusMessage = message;
        statusIsError = isError;
    }

    private List<(string Name, string Status)>? onlineFriends;

    private unsafe void DrawDebugTab()
    {
        var name = Plugin.PlayerState.CharacterName ?? "Not logged in";
        var contentId = PlayerState.Instance()->ContentId;

        ImGui.TextUnformatted("Character Name:");
        ImGui.SameLine();
        ImGui.TextUnformatted(name);

        ImGui.Spacing();

        ImGui.TextUnformatted("Character ID:");
        ImGui.SameLine();
        var contentIdStr = contentId.ToString();
        ImGui.TextUnformatted(contentIdStr);
        ImGui.SameLine();
        if (ImGui.Button("Copy##contentId"))
        {
            ImGui.SetClipboardText(contentIdStr);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Online Friends:");
        ImGui.SameLine();
        if (ImGui.Button("Test Delegate##delegate"))
        {
            Helpers.RefreshPlayerFreindsList();
        }
        if (ImGui.Button("Refresh##onlineFriends"))
        {
            onlineFriends = Helpers.GetOnlineFriends();
        }

        if (onlineFriends == null)
        {
            ImGui.TextDisabled("Press Refresh to load.");
        }
        else if (onlineFriends.Count == 0)
        {
            ImGui.TextDisabled("No online friends found (open the in-game Friend List once to populate).");
        }
        else
        {
            foreach (var (friendName, friendStatus) in onlineFriends)
            {
                ImGui.BulletText($"{friendName} ({friendStatus})");
            }
        }
    }

    private void DrawChannelToggle(string label, XivChatType type)
    {
        var enabled = configuration.EnabledChannels.GetValueOrDefault(type, false);
        if (ImGui.Checkbox(label, ref enabled))
        {
            configuration.EnabledChannels[type] = enabled;
            configuration.Save();
            plugin.ChatListenerService.ScheduleReconnect();
        }
    }
}
