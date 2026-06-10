using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XIVRemoteChat.Services;

public sealed class TokenRenewalService : IDisposable
{
    private readonly Configuration configuration;

    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private Timer? renewalTimer;

    private const string RenewUrl = $"{Plugin.ServerBaseUrl}/auth/renew";
    private static readonly TimeSpan RenewalBuffer = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public TokenRenewalService(Configuration configuration)
    {
        this.configuration = configuration;
        ScheduleRenewal();
    }

    public void ScheduleRenewal()
    {
        renewalTimer?.Dispose();
        renewalTimer = null;

        var token = configuration.Token;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }


        var expiry = ParseExpiry(token);
        if (expiry is null)
        {
            Plugin.Log.Warning("[TokenRenewal] Could not parse token expiry — renewal not scheduled.");
            return;
        }

        var delay = expiry.Value - DateTimeOffset.UtcNow - RenewalBuffer;

        var minDelay = TimeSpan.FromSeconds(5);
        if (delay < minDelay)
        {
            delay = minDelay;
        }

        Plugin.Log.Information($"[TokenRenewal] Renewal scheduled in {delay:d\\:hh\\:mm\\:ss}.");
        renewalTimer = new Timer(async _ => await RenewAsync(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private async Task RenewAsync()
    {
        try
        {
            var currentToken = configuration.Token;
            if (string.IsNullOrEmpty(currentToken))
            {
                return;
            }


            using var request = new HttpRequestMessage(HttpMethod.Get, RenewUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Plugin.Log.Error("[TokenRenewal] Token rejected (401) — clearing token, please log in again.");
                configuration.Token = null;
                configuration.Save();
                return;
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                Plugin.Log.Error("[TokenRenewal] Renewal response missing 'token' field.");
                ScheduleRetry();
                return;
            }

            var newToken = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(newToken))
            {
                Plugin.Log.Error("[TokenRenewal] Renewal response contained an empty token.");
                ScheduleRetry();
                return;
            }

            configuration.Token = newToken;
            configuration.Save();
            Plugin.Log.Information("[TokenRenewal] Token renewed successfully.");
            ScheduleRenewal();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[TokenRenewal] Renewal failed: {ex.Message}");
            ScheduleRetry();
        }
    }

    private void ScheduleRetry()
    {
        renewalTimer?.Dispose();
        Plugin.Log.Warning($"[TokenRenewal] Retrying in {RetryDelay.TotalMinutes:0} minute(s).");
        renewalTimer = new Timer(async _ => await RenewAsync(), null, RetryDelay, Timeout.InfiniteTimeSpan);
    }

    private static DateTimeOffset? ParseExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }


        try
        {
            var payload = parts[1];
            var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
            var json = Encoding.UTF8.GetString(bytes);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement) && expElement.TryGetDouble(out var exp))
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)exp);
            }

        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        renewalTimer?.Dispose();
        httpClient.Dispose();
    }
}
