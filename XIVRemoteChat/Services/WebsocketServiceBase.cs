
using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XIVRemoteChat.Services;

public abstract class WebsocketServiceBase(Configuration configuration, bool expectsMessages = false) : IDisposable
{
    protected readonly Configuration configuration = configuration;
    private ClientWebSocket? webSocket;
    private CancellationTokenSource? wsCts;
    protected const int MaxChunkBytes = 256;
    private readonly int bufferSize = expectsMessages ? 4096 : 256;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim connectLock = new(1, 1);
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private Uri? websocketUri = null;
    private string? authToken = null;

    public abstract Task SetupAndConnect();

    protected void SetWebsocketUri(string uri)
    {
        websocketUri = new Uri(uri);
    }

    protected void SetAuthToken(string token)
    {
        authToken = token;
    }

    protected async Task ConnectAsync()
    {
        await connectLock.WaitAsync();
        try
        {
            wsCts?.Cancel();
            wsCts?.Dispose();
            wsCts = new CancellationTokenSource();

            if (websocketUri == null)
            {
                Plugin.Log.Error("WebSocket URI not set — cannot connect.");
                return;
            }

            webSocket?.Dispose();
            webSocket = new ClientWebSocket();
            webSocket.Options.CollectHttpResponseDetails = true;
            if (authToken != null)
            {
                webSocket.Options.SetRequestHeader("Authorization", $"Bearer {authToken}");
            }

            var socket = webSocket;
            var ct = wsCts.Token;
            try
            {
                await socket.ConnectAsync(websocketUri, ct);
                Plugin.Log.Information("WebSocket connected.");
                _ = Task.Run(() => ReceiveLoop(socket, ct));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (socket.HttpStatusCode == HttpStatusCode.Unauthorized)
                {
                    Plugin.Log.Error("WebSocket authentication failed (401) — clearing token, please log in again.");
                    configuration.Token = null;
                    configuration.Save();
                    return;
                }

                Plugin.Log.Error($"WebSocket connection failed: {ex.Message} — retrying in {ReconnectDelay.TotalSeconds:0}s.");
                _ = Task.Run(() => ReconnectAfterDelay(ct));
            }
        }
        finally
        {
            connectLock.Release();
        }
    }

    private async Task ReconnectAfterDelay(CancellationToken ct)
    {
        try
        {
            await Task.Delay(ReconnectDelay, ct);
        }
        catch (OperationCanceledException) { return; }

        if (!ct.IsCancellationRequested)
        {
            await SetupAndConnect();
        }
    }

    private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new ArraySegment<byte>(new byte[bufferSize]);
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer.Array!, buffer.Offset, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    try
                    {
                        ProcessMessage(Encoding.UTF8.GetString(ms.ToArray()));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"WebSocket message handler failed: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"WebSocket error: {ex.Message}");
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        Plugin.Log.Warning($"WebSocket closed unexpectedly, reconnecting in {ReconnectDelay.TotalSeconds:0}s...");
        await ReconnectAfterDelay(ct);
    }

    protected abstract void ProcessMessage(string v);

    public async Task DisconnectAsync()
    {
        wsCts?.Cancel();

        await connectLock.WaitAsync();
        try
        {
            if (webSocket is { State: WebSocketState.Open })
            {
                try
                {
                    using var closeCts = new CancellationTokenSource(CloseTimeout);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Logout", closeCts.Token);
                }
                catch { }
            }

            webSocket?.Dispose();
            webSocket = null;
            Plugin.Log.Information("WebSocket disconnected.");
        }
        finally
        {
            connectLock.Release();
        }
    }

    protected async Task SendBatchAsync(string json)
    {
        var socket = webSocket;
        if (socket?.State != WebSocketState.Open)
        {
            Plugin.Log.Warning("WebSocket not connected — cannot send json.");
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);

        await sendLock.WaitAsync();
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
        finally
        {
            sendLock.Release();
        }
    }

    public bool IsConnected => webSocket?.State == WebSocketState.Open;

    public WebSocketState? State => webSocket?.State;

    protected virtual void CleanUp()
    {
    }

    public void Dispose()
    {
        Plugin.Log.Information("Disposing WebSocket service...");
        CleanUp();

        wsCts?.Cancel();
        try { webSocket?.Abort(); } catch { /* already dead */ }
        webSocket?.Dispose();
        webSocket = null;
        wsCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
