using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace XIVRemoteChat.Services;

internal sealed class AuthCallbackServer : IDisposable
{
    private const int Port = 11987;

    private readonly Configuration configuration;
    private readonly Action onSuccess;
    private TcpListener? listener;
    private CancellationTokenSource? cts;

    public bool IsRunning => listener != null;

    internal AuthCallbackServer(Configuration configuration, Action onSuccess)
    {
        this.configuration = configuration;
        this.onSuccess = onSuccess;
    }

    public void Start()
    {
        if (IsRunning) return;

        cts = new CancellationTokenSource();
        listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();

        _ = Task.Run(() => ListenAsync(cts.Token));
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener!.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch { break; }

                try
                {
                    await HandleClientAsync(client, ct);
                }
                catch { }
                finally
                {
                    client.Dispose();
                }
            }
        }
        finally
        {
            Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync(ct);
        if (requestLine == null) return;

        while (await reader.ReadLineAsync(ct) is { Length: > 0 }) { }

        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            await WriteResponseAsync(stream, 400, "Bad Request", ct);
            return;
        }

        var path = parts[1];
        var questionMark = path.IndexOf('?');
        var segment = questionMark >= 0 ? path[..questionMark] : path;
        var query = questionMark >= 0 ? HttpUtility.ParseQueryString(path[(questionMark + 1)..]) : null;

        if (segment == "/auth/accept")
        {
            var token = query?["token"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                await WriteResponseAsync(stream, 200, "You can now close this window.", ct);

                configuration.Token = token;
                configuration.Save();
                onSuccess();
                return;
            }
        }

        await WriteResponseAsync(stream, 404, "Not Found", ct);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string body, CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {statusCode} OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes, ct);
        await stream.WriteAsync(bodyBytes, ct);
    }

    public void Stop()
    {
        cts?.Cancel();
        try { listener?.Server.Close(); } catch { }
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    public void Dispose() => Stop();
}
