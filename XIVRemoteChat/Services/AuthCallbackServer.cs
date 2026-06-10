using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XIVRemoteChat.Services;

internal sealed class AuthCallbackServer : IDisposable
{
    private const int Port = 11987;

    private readonly Configuration configuration;
    private readonly Action onSuccess;
    private HttpListener? listener;
    private CancellationTokenSource? cts;

    public bool IsRunning => listener?.IsListening ?? false;

    internal AuthCallbackServer(Configuration configuration, Action onSuccess)
    {
        this.configuration = configuration;
        this.onSuccess = onSuccess;
    }

    public void Start()
    {
        if (IsRunning) return;

        cts = new CancellationTokenSource();
        listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{Port}/");
        listener.Start();

        _ = Task.Run(() => ListenAsync(cts.Token));
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener!.GetContextAsync().WaitAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch { break; }

                var req = ctx.Request;
                var res = ctx.Response;

                try
                {
                    if (req.Url?.AbsolutePath == "/auth/accept")
                    {
                        var token = req.QueryString["token"];
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            var body = Encoding.UTF8.GetBytes("You can now close this window.");
                            res.StatusCode = 200;
                            res.ContentType = "text/plain; charset=utf-8";
                            res.ContentLength64 = body.Length;
                            await res.OutputStream.WriteAsync(body, ct);
                            res.Close();

                            configuration.Token = token;
                            configuration.Save();
                            onSuccess();
                            return;
                        }
                    }

                    res.StatusCode = 404;
                    res.Close();
                }
                catch
                {
                    try { res.Close(); } catch { }
                }
            }
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    public void Dispose() => Stop();
}
