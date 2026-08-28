using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Services;

public sealed record CachedIcon(byte[] Bytes, string ContentType);

public sealed class EventIconCache {
    private readonly HttpClient _http;
    private readonly ILogger<EventIconCache>? _logger;
    private readonly string? _baseUrl;
    private readonly string? _apiKey;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CachedIcon> _icons = [];

    public EventIconCache(HttpClient http, string? baseUrl, string? apiKey, ILogger<EventIconCache>? logger = null) {
        _http = http;
        _logger = logger;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public bool IsConfigured => _baseUrl is not null;

    public int Count {
        get {
            lock (_gate) {
                return _icons.Count;
            }
        }
    }

    public static string KeyFor(string type, bool ultra) =>
        type.ToLowerInvariant() + (ultra ? "|ultra" : "");

    public CachedIcon? Get(string type, bool ultra) {
        lock (_gate) {
            return _icons.GetValueOrDefault(KeyFor(type, ultra));
        }
    }

    public async Task WarmAsync(IEnumerable<(string Type, bool Ultra)> keys, CancellationToken cancellationToken) {
        if (!IsConfigured) {
            return;
        }

        var missing = new List<(string Type, bool Ultra)>();
        lock (_gate) {
            foreach (var key in keys) {
                if (!_icons.ContainsKey(KeyFor(key.Type, key.Ultra))) {
                    missing.Add(key);
                }
            }
        }

        foreach (var key in missing) {
            await FetchOneAsync(key.Type, key.Ultra, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FetchOneAsync(string type, bool ultra, CancellationToken cancellationToken) {
        var url = _baseUrl + "/api/v1/data/asset/event-icon?name=" + Uri.EscapeDataString(type) + (ultra ? "&cc=1" : "");
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (_apiKey is not null) {
                request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/png";
            lock (_gate) {
                _icons[KeyFor(type, ultra)] = new CachedIcon(bytes, contentType);
            }
        } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) {
            _logger?.LogWarning(ex, "event icon fetch failed for {Type} (ultra={Ultra})", type, ultra);
        }
    }
}
