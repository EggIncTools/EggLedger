using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggLedger.Desktop.Update;

public sealed class HandshakeListener : IDisposable {
    private readonly HttpListener _listener;
    private readonly string _token;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _served =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _handoffDone;
    private CancellationTokenSource? _cts;

    private HandshakeListener(HttpListener listener, string token, string address, ILogger logger) {
        _listener = listener;
        _token = token;
        _logger = logger;
        Address = address;
    }

    public string Address { get; }

    public Task Served => _served.Task;

    public static HandshakeListener Start(string token, ILogger? logger = null) {
        var port = FindFreeLoopbackPort();
        var listener = new HttpListener();
        var address = $"127.0.0.1:{port}";
        listener.Prefixes.Add($"http://{address}/");
        listener.Start();

        var self = new HandshakeListener(listener, token, address, logger ?? NullLogger<HandshakeListener>.Instance) {
            _cts = new CancellationTokenSource()
        };
        _ = self.ServeLoopAsync(self._cts.Token);
        return self;
    }

    private static int FindFreeLoopbackPort() {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeLoopAsync(CancellationToken cancel) {
        while (!cancel.IsCancellationRequested) {
            HttpListenerContext ctx;
            try {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) {
                _logger.LogDebug(ex, "update: handshake listener stopped");
                return;
            }

            HandleRequest(ctx);
        }
    }

    private void HandleRequest(HttpListenerContext ctx) {
        var response = ctx.Response;
        try {
            var isReady = string.Equals(ctx.Request.Url?.AbsolutePath, "/ready", StringComparison.Ordinal);
            var token = ctx.Request.QueryString["token"];

            if (!isReady) {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            if (!string.Equals(token, _token, StringComparison.Ordinal)) {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            if (Interlocked.CompareExchange(ref _handoffDone, 1, 0) == 0) {
                _served.TrySetResult();
            }
        } finally {
            try {
                response.Close();
            } catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) {
                _logger.LogDebug(ex, "update: handshake response close failed");
            }
        }
    }

    public void Dispose() {
        _cts?.Cancel();
        _cts?.Dispose();
        try {
            _listener.Close();
        } catch (HttpListenerException ex) {
            _logger.LogDebug(ex, "update: handshake listener close failed");
        }
    }
}
