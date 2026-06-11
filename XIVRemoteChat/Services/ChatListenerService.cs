using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

using XIVRemoteChat.Models;

namespace XIVRemoteChat.Services;

public sealed class ChatListenerService : WebsocketServiceBase
{
    private const int MaxDebounceMs = 1000;
    private const int ReconnectDebounceMs = 1000;
    private const int MaxPendingMessages = 200;
    private readonly ConcurrentQueue<SocketChatMessageDto> pending = new();
    private readonly Plugin plugin;

    private int timerRunning = 0;
    private Timer? debounceTimer;
    private Timer? reconnectDebounceTimer;


    public ChatListenerService(Plugin plugin) : base(plugin.Configuration)
    {
        this.plugin = plugin;
        Plugin.ChatGui.ChatMessage += OnChatMessage;
        Plugin.ClientState.Login += OnLogin;
        Plugin.ClientState.Logout += OnLogout;

        if (!string.IsNullOrEmpty(Plugin.PlayerState.CharacterName))
        {
            Task.Run(SetupAndConnect);
        }
    }

    public override async Task SetupAndConnect()
    {
        if (configuration.Token == null)
        {
            Plugin.Log.Warning("[ChatListener] Cannot connect — not logged in to service.");
            return;
        }

        var url = $"{Plugin.WebSocketBaseUrl}/characters/socket"
                  + $"?characterId={Uri.EscapeDataString(Helpers.GetLocalContentId())}"
                  + $"&name={Uri.EscapeDataString(Plugin.PlayerState.CharacterName)}"
                  + $"&channels={Uri.EscapeDataString(string.Join(",", GetEnabledChannelNames()))}";
        SetWebsocketUri(url);
        SetAuthToken(configuration.Token);
        await ConnectAsync();
        plugin.FriendListRefreshService.SendNow();
    }

    public void Connect() => Task.Run(SetupAndConnect);

    public void ScheduleReconnect()
    {
        if (configuration.Token == null || string.IsNullOrEmpty(Plugin.PlayerState.CharacterName))
        {
            return;
        }

        reconnectDebounceTimer?.Dispose();
        reconnectDebounceTimer = new Timer(async _ =>
        {
            await DisconnectAsync();
            await SetupAndConnect();
        }, null, ReconnectDebounceMs, Timeout.Infinite);
    }

    private void OnLogin() => Task.Run(SetupAndConnect);
    private void OnLogout(int type, int code) => Task.Run(DisconnectAsync);

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!configuration.EnabledChannels.GetValueOrDefault(message.LogKind, false))
        {
            return;
        }

        var localPlayer = Plugin.PlayerState;
        var senderName = GetSenderWithWorld(message.Sender);
        var isSelf = message.LogKind == XivChatType.TellOutgoing
                     || (!string.IsNullOrEmpty(localPlayer.CharacterName)
                         && senderName.Contains(localPlayer.CharacterName, StringComparison.Ordinal));

        var messageText = Helpers.ConvertSeString(message.Message);

        var trueSenderName = isSelf ? Plugin.PlayerState.CharacterName : senderName;

        pending.Enqueue(new SocketChatMessageDto(
            Channel: GetChannelName(message.LogKind, senderName),
            SenderName: configuration.HasEncryptionSettings ? Helpers.Encrypt(trueSenderName, configuration.EncryptionPassword!, configuration.EncryptionSalt!) : trueSenderName,
            Message: configuration.HasEncryptionSettings ? Helpers.Encrypt(messageText, configuration.EncryptionPassword!, configuration.EncryptionSalt!) : messageText,
            Self: isSelf,
            PostedDate: DateTime.UtcNow
        ));

        if (Interlocked.CompareExchange(ref timerRunning, 1, 0) == 0)
        {
            debounceTimer?.Dispose();
            debounceTimer = new Timer(async _ => await FlushAsync(), null, MaxDebounceMs, Timeout.Infinite);
        }
    }

    private async Task FlushAsync()
    {
        try
        {
            await FlushCoreAsync();
        }
        finally
        {
            Interlocked.Exchange(ref timerRunning, 0);

            if (!pending.IsEmpty && Interlocked.CompareExchange(ref timerRunning, 1, 0) == 0)
            {
                debounceTimer?.Dispose();
                debounceTimer = new Timer(async _ => await FlushAsync(), null, MaxDebounceMs, Timeout.Infinite);
            }
        }
    }

    private async Task FlushCoreAsync()
    {
        if (pending.IsEmpty)
        {
            return;
        }

        if (!IsConnected)
        {
            while (pending.Count > MaxPendingMessages && pending.TryDequeue(out _)) { }
            Plugin.Log.Warning("[ChatListener] WebSocket not connected — holding messages until reconnect.");
            Connect();
            return;
        }

        var messages = new List<SocketChatMessageDto>();
        while (pending.TryDequeue(out var msg))
        {
            messages.Add(msg);
        }

        try
        {
            var batch = new List<SocketChatMessageDto>();
            var batchBytes = 2;
            foreach (var msg in messages)
            {
                var msgBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(msg)) + 1;
                if (batch.Count > 0 && batchBytes + msgBytes > MaxChunkBytes)
                {
                    await SendBatchAsync(JsonSerializer.Serialize(batch));
                    batch.Clear();
                    batchBytes = 2;
                }

                batch.Add(msg);
                batchBytes += msgBytes;
            }

            if (batch.Count > 0)
            {
                await SendBatchAsync(JsonSerializer.Serialize(batch));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ChatListener] WebSocket send failed: {ex.Message}");
        }
    }

    private List<string> GetEnabledChannelNames()
    {
        var names = new List<string>();
        foreach (var (type, enabled) in configuration.EnabledChannels)
        {
            if (!enabled)
            {
                continue;
            }

            var name = type switch
            {
                XivChatType.TellIncoming or XivChatType.TellOutgoing => "DM",
                XivChatType.CustomEmote or XivChatType.StandardEmote => "Emote",
                _ => type.ToString(),
            };

            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }
        return names;
    }

    private static string GetChannelName(XivChatType type, string senderName) => type switch
    {
        XivChatType.TellIncoming => $"DM: {senderName}",
        XivChatType.TellOutgoing => $"DM: {senderName}",
        XivChatType.CustomEmote => "Emote",
        XivChatType.StandardEmote => "Emote",
        _ => type.ToString(),
    };

    private static string GetSenderWithWorld(SeString seString)
    {
        foreach (var payload in seString.Payloads)
        {
            if (payload is PlayerPayload player)
            {
                var worldName = player.World.ValueNullable?.Name.ToString() ?? player.World.RowId.ToString();
                return $"{player.PlayerName}@{worldName}";
            }
        }
        return Helpers.ConvertSeString(seString);
    }

    protected override void CleanUp()
    {
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ClientState.Logout -= OnLogout;

        debounceTimer?.Dispose();
        reconnectDebounceTimer?.Dispose();
    }

    protected override void ProcessMessage(string json)
    {
    }

}
