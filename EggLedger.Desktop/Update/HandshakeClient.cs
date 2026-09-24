using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggLedger.Desktop.Update;

public sealed class HandshakeClient(HttpClient httpClient, ILogger<HandshakeClient>? logger = null) {
    public const int MaxPingAttempts = 5;

    public static readonly TimeSpan PingRetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<HandshakeClient> _logger = logger ?? NullLogger<HandshakeClient>.Instance;

    public async Task<bool> PingOldReadyAsync(string addr, string token, CancellationToken cancel = default) {
        var url = $"http://{addr}/ready?token={token}";
        for (var i = 0; i < MaxPingAttempts; i++) {
            try {
                using var content = new StringContent("");
                using var resp = await _httpClient.PostAsync(url, content, cancel).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) {
                    return true;
                }
            } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
                _logger.LogDebug(ex, "update: handshake ping attempt {Attempt} failed", i + 1);
            }

            if (cancel.IsCancellationRequested) {
                return false;
            }
            await Task.Delay(PingRetryDelay, cancel).ConfigureAwait(false);
        }
        return false;
    }
}
