using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using XIVRemoteChat.Models;

namespace XIVRemoteChat.Services;

public sealed class SendListenerService : WebsocketServiceBase
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    private const string SocketPath = "/send/socket";

    public SendListenerService(Configuration configuration) : base(configuration, expectsMessages: true)
    {
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
            Plugin.Log.Warning("[SendListener] Cannot connect — not logged in to service.");
            return;
        }

        var url = $"{Plugin.WebSocketBaseUrl}{SocketPath}";
        SetWebsocketUri(url);
        SetAuthToken(configuration.Token);
        await ConnectAsync();
    }

    public void Connect() => Task.Run(SetupAndConnect);

    private void OnLogin() => Task.Run(SetupAndConnect);
    private void OnLogout(int type, int code) => Task.Run(DisconnectAsync);

    protected override void ProcessMessage(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<SendMessageDto>(json, jsonOptions);
            if (dto == null)
            {
                return;
            }

            Plugin.Log.Debug($"[SendListener] Received send request for channel '{dto.Channel}'.");
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    var localId = Helpers.GetLocalContentId();
                    if (dto.CharacterId != localId)
                    {
                        Plugin.Log.Debug($"[SendListener] CharacterId mismatch — message has '{dto.CharacterId}', local is '{localId}'. Skipping.");
                        return;
                    }

                    var command = BuildCommand(dto.Channel, Chat.SanitiseText(dto.Message));
                    if (command == null)
                    {
                        Plugin.Log.Warning($"[SendListener] Unknown channel '{dto.Channel}'. Skipping.");
                        return;
                    }

                    if (Encoding.UTF8.GetByteCount(command) > 500)
                    {
                        Plugin.Log.Warning("[SendListener] Message too long to send. Skipping.");
                        return;
                    }

                    Chat.SendMessage(command);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[SendListener] Failed to send chat message: {ex.Message}");
                }
            });
        }
        catch (JsonException ex)
        {
            Plugin.Log.Error($"[SendListener] Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[SendListener] Failed to process message: {ex.Message}");
        }
    }

    private static string? BuildCommand(string channel, string message)
    {
        if (message.StartsWith('/'))
        {
            return message;
        }

        if (channel.StartsWith("DM: ", StringComparison.OrdinalIgnoreCase))
        {
            var target = channel["DM: ".Length..];
            return $"/tell {target} {message}";
        }

        return channel switch
        {
            "Say" => $"/say {message}",
            "Shout" => $"/shout {message}",
            "Yell" => $"/yell {message}",
            "Party" => $"/p {message}",
            "Alliance" => $"/a {message}",
            "FreeCompany" => $"/fc {message}",
            "NoviceNetwork" => $"/n {message}",
            "PvPTeam" => $"/pvpteam {message}",
            "Emote" => $"/em {message}",
            "Ls1" => $"/l1 {message}",
            "Ls2" => $"/l2 {message}",
            "Ls3" => $"/l3 {message}",
            "Ls4" => $"/l4 {message}",
            "Ls5" => $"/l5 {message}",
            "Ls6" => $"/l6 {message}",
            "Ls7" => $"/l7 {message}",
            "Ls8" => $"/l8 {message}",
            "CrossLinkShell1" => $"/cwl1 {message}",
            "CrossLinkShell2" => $"/cwl2 {message}",
            "CrossLinkShell3" => $"/cwl3 {message}",
            "CrossLinkShell4" => $"/cwl4 {message}",
            "CrossLinkShell5" => $"/cwl5 {message}",
            "CrossLinkShell6" => $"/cwl6 {message}",
            "CrossLinkShell7" => $"/cwl7 {message}",
            "CrossLinkShell8" => $"/cwl8 {message}",
            _ => null,
        };
    }

    protected override void CleanUp()
    {
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ClientState.Logout -= OnLogout;
    }
}
