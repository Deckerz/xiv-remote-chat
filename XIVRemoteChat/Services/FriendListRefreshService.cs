using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Dalamud.Plugin.Services;

using XIVRemoteChat.Models;

namespace XIVRemoteChat.Services;

public sealed class FriendListRefreshService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PostDelay = TimeSpan.FromSeconds(2);

    private readonly Configuration configuration;

    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private DateTime? nextRefresh;
    private DateTime? nextPost;

    public FriendListRefreshService(Configuration configuration)
    {
        this.configuration = configuration;

        Plugin.ClientState.Login += OnLogin;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.Framework.Update += OnFrameworkUpdate;

        if (Plugin.ClientState.IsLoggedIn)
        {
            nextRefresh = DateTime.UtcNow + InitialDelay;
        }
    }

    private void OnLogin() => nextRefresh = DateTime.UtcNow + InitialDelay;

    private void OnLogout(int type, int code)
    {
        nextRefresh = null;
        nextPost = null;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;

        if (nextRefresh != null && now >= nextRefresh)
        {
            nextRefresh = now + RefreshInterval;

            try
            {
                Helpers.RefreshPlayerFreindsList();
                nextPost = now + PostDelay;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[FriendListRefresh] Refresh failed: {ex.Message}");
            }
        }

        if (nextPost != null && now >= nextPost)
        {
            nextPost = null;

            var friends = Helpers.GetOnlineFriends();
            var characterId = Helpers.GetLocalContentId();
            _ = Task.Run(() => PostFriendsAsync(characterId, friends));
        }
    }

    private async Task PostFriendsAsync(string characterId, List<(string Name, string Status)> friends)
    {
        var token = configuration.Token;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            var dtos = new List<FriendDto>(friends.Count);
            foreach (var (name, status) in friends)
            {
                dtos.Add(new FriendDto(name, status));
            }

            var url = $"{Plugin.ServerBaseUrl}/characters/{Uri.EscapeDataString(characterId)}/friends";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(dtos), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Plugin.Log.Information($"[FriendListRefresh] Posted {dtos.Count} friend(s).");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[FriendListRefresh] Posting friends failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ClientState.Logout -= OnLogout;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        httpClient.Dispose();
    }

    internal void SendNow()
    {
        nextRefresh = DateTime.UtcNow.AddSeconds(-1);
        nextPost = DateTime.UtcNow.AddSeconds(2);
    }

}
