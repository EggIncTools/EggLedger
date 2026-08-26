using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Services;

public sealed record GameEvent {
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Message { get; init; }
    public double Multiplier { get; init; }
    public bool Ultra { get; init; }
    public double StartTimestamp { get; init; }
    public double EndTimestamp { get; init; }
    public string? Source { get; init; }

    [JsonIgnore]
    public bool IsDeviceSourced =>
        string.Equals(Source, GameEventsService.DeviceSource, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public DateTimeOffset StartsAt => FromUnixSeconds(StartTimestamp);

    [JsonIgnore]
    public DateTimeOffset EndsAt => FromUnixSeconds(EndTimestamp);

    private static DateTimeOffset FromUnixSeconds(double seconds) {
        if (double.IsNaN(seconds)) {
            return DateTimeOffset.UnixEpoch;
        }
        const double minSeconds = -62135596800d;
        const double maxSeconds = 253402300799d;
        return DateTimeOffset.UnixEpoch.AddSeconds(Math.Clamp(seconds, minSeconds, maxSeconds));
    }
}

public sealed record GameEventsResponse {
    public int Total { get; init; }
    public IReadOnlyList<GameEvent> Events { get; init; } = [];
}

public interface IGameEventStore {
    Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(byte[] utf8Json, CancellationToken cancellationToken = default);
}

internal readonly record struct GameEventKey(string Id, double StartTimestamp);

internal sealed record GameEventsError {
    public string? Error { get; init; }
    public double RetryAfterSeconds { get; init; }
}

public sealed class GameEventsService {
    public const string DeviceSource = "device";
    public const string BaseUrlVariable = "EGI_BASE_URL";
    public const string ApiKeyVariable = "EGI_API_KEY";
    public const string ApiKeyHeader = "X-Api-Key";
    public const string EndpointPath = "/api/v1/events";

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RefreshFloor = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan MergeWindow = TimeSpan.FromHours(48);
    internal const int PageLimit = 1000;

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);
    private const int MaxPages = 20;
    private const int MaxBackoffDoublings = 8;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IGameEventStore? _store;
    private readonly ILogger<GameEventsService>? _logger;
    private readonly TimeProvider _clock;
    private readonly string? _baseUrl;
    private readonly string? _apiKey;
    private readonly string _endpoint = "";
    private readonly Lock _gate = new();
    private readonly Dictionary<GameEventKey, GameEvent> _events = [];
    private DateTimeOffset _nextAllowedRefresh = DateTimeOffset.MinValue;
    private Task? _inFlight;
    private int _consecutiveUnavailable;
    private bool _loaded;

    public GameEventsService(
        HttpClient http,
        string? baseUrl,
        string? apiKey = null,
        IGameEventStore? store = null,
        ILogger<GameEventsService>? logger = null,
        TimeProvider? clock = null) {
        _http = http;
        _store = store;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl)) {
            return;
        }
        var trimmed = baseUrl.Trim().TrimEnd('/');
        var candidate = trimmed + EndpointPath;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out _)) {
            _logger?.LogWarning("game events base url is not an absolute uri: {BaseUrl}", trimmed);
            return;
        }
        _baseUrl = trimmed;
        _endpoint = candidate;
    }

    public bool IsConfigured => _baseUrl is not null;

    public bool HasData {
        get {
            lock (_gate) {
                return _events.Count > 0;
            }
        }
    }

    public int Count {
        get {
            lock (_gate) {
                return _events.Count;
            }
        }
    }

    public DateTimeOffset NextAllowedRefresh {
        get {
            lock (_gate) {
                return _nextAllowedRefresh;
            }
        }
    }

    public IReadOnlyList<GameEvent> ActiveAt(DateTimeOffset instant) {
        double seconds = instant.ToUnixTimeMilliseconds() / 1000.0;
        var matches = new List<GameEvent>();
        lock (_gate) {
            foreach (var candidate in _events.Values) {
                if (candidate.StartTimestamp <= seconds && seconds <= candidate.EndTimestamp) {
                    matches.Add(candidate);
                }
            }
        }
        matches.Sort(NewestFirst);
        return matches;
    }

    internal IReadOnlyList<GameEvent> Snapshot() {
        List<GameEvent> all;
        lock (_gate) {
            all = [.. _events.Values];
        }
        all.Sort(NewestFirst);
        return all;
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) {
        if (!IsConfigured) {
            return Task.CompletedTask;
        }
        lock (_gate) {
            if (_loaded) {
                return Task.CompletedTask;
            }
            if (_inFlight is { IsCompleted: false } running) {
                return running;
            }
            _inFlight = Task.Run(() => RunAsync(fromStore: true, cancellationToken), CancellationToken.None);
            return _inFlight;
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) {
        if (!IsConfigured) {
            return Task.CompletedTask;
        }
        lock (_gate) {
            if (_inFlight is { IsCompleted: false } running) {
                return running;
            }
            if (_clock.GetUtcNow() < _nextAllowedRefresh) {
                return Task.CompletedTask;
            }
            _inFlight = Task.Run(() => RunAsync(fromStore: false, cancellationToken), CancellationToken.None);
            return _inFlight;
        }
    }

    private async Task RunAsync(bool fromStore, CancellationToken cancellationToken) {
        bool servedFromStore = false;
        try {
            servedFromStore = fromStore
                && await TryLoadStoredAsync(cancellationToken).ConfigureAwait(false);
            if (servedFromStore) {
                return;
            }
            await FetchAsync(cancellationToken).ConfigureAwait(false);
        } finally {
            if (!servedFromStore) {
                Gate(TimeSpan.Zero);
            }
            _loaded = true;
        }
    }

    private void Gate(TimeSpan penalty) {
        var candidate = _clock.GetUtcNow() + (penalty > RefreshFloor ? penalty : RefreshFloor);
        lock (_gate) {
            if (candidate > _nextAllowedRefresh) {
                _nextAllowedRefresh = candidate;
            }
        }
    }

    private async Task<bool> TryLoadStoredAsync(CancellationToken cancellationToken) {
        if (_store is null) {
            return false;
        }
        try {
            var bytes = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) {
                return false;
            }
            var parsed = Parse(bytes);
            if (parsed is null || parsed.Events.Count == 0) {
                return false;
            }
            Merge(parsed.Events);
            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            _logger?.LogWarning(ex, "game events store read failed");
            return false;
        }
    }

    private async Task FetchAsync(CancellationToken cancellationToken) {
        double? after = ComputeAfter();
        var collected = new List<GameEvent>();
        int offset = 0;

        for (int page = 0; page < MaxPages; page++) {
            var response = await GetPageAsync(after, offset, cancellationToken).ConfigureAwait(false);
            if (response is null) {
                if (collected.Count == 0) {
                    return;
                }
                break;
            }
            collected.AddRange(response.Events);
            if (response.Events.Count == 0 || collected.Count >= response.Total) {
                break;
            }
            offset += PageLimit;
        }

        bool changed = Merge(collected);
        if (changed) {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private double? ComputeAfter() {
        double newest;
        lock (_gate) {
            if (_events.Count == 0) {
                return null;
            }
            newest = _events.Values.Max(candidate => candidate.StartTimestamp);
        }
        if (double.IsInfinity(newest) || double.IsNaN(newest)) {
            return null;
        }
        double nowSeconds = _clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0;
        double cursor = Math.Min(newest, nowSeconds) - MergeWindow.TotalSeconds;
        return cursor > 0 ? cursor : 0;
    }

    private async Task<GameEventsResponse?> GetPageAsync(
        double? after, int offset, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(after, offset));
        if (_apiKey is not null) {
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);
        }

        try {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests) {
                var wait = await ReadRetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
                _logger?.LogWarning(
                    "game events rate limited by {BaseUrl}, waiting {Seconds}s", _baseUrl, wait.TotalSeconds);
                Gate(wait);
                return null;
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) {
                var wait = NextUnavailableBackoff();
                _logger?.LogWarning(
                    "game events unavailable at {BaseUrl}, backing off {Seconds}s", _baseUrl, wait.TotalSeconds);
                Gate(wait);
                return null;
            }

            _consecutiveUnavailable = 0;

            if (!response.IsSuccessStatusCode) {
                _logger?.LogWarning(
                    "game events request to {BaseUrl} failed with status {Status}",
                    _baseUrl, (int)response.StatusCode);
                Gate(TimeSpan.Zero);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var parsed = Parse(bytes);
            if (parsed is null) {
                Gate(TimeSpan.Zero);
            }
            return parsed;
        } catch (Exception ex) when (ex is HttpRequestException or IOException) {
            _logger?.LogWarning(ex, "game events request to {BaseUrl} failed", _baseUrl);
            Gate(TimeSpan.Zero);
            return null;
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            _logger?.LogWarning(ex, "game events request to {BaseUrl} timed out", _baseUrl);
            Gate(TimeSpan.Zero);
            return null;
        }
    }

    private TimeSpan NextUnavailableBackoff() {
        int attempt = Math.Min(++_consecutiveUnavailable, MaxBackoffDoublings);
        double seconds = RefreshFloor.TotalSeconds * Math.Pow(2, attempt - 1);
        var backoff = TimeSpan.FromSeconds(seconds);
        return backoff > MaxBackoff ? MaxBackoff : backoff;
    }

    private async Task<TimeSpan> ReadRetryAfterAsync(
        HttpResponseMessage response, CancellationToken cancellationToken) {
        try {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length > 0
                && JsonSerializer.Deserialize<GameEventsError>(bytes, Json) is { RetryAfterSeconds: > 0 } error) {
                return ClampWait(error.RetryAfterSeconds);
            }
        } catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException) {
            _logger?.LogWarning(ex, "game events rate limit body from {BaseUrl} was not readable", _baseUrl);
        }

        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta && delta > TimeSpan.Zero) {
            return ClampWait(delta.TotalSeconds);
        }
        if (header?.Date is { } date) {
            var untilDate = date - _clock.GetUtcNow();
            if (untilDate > TimeSpan.Zero) {
                return ClampWait(untilDate.TotalSeconds);
            }
        }
        return DefaultRetryAfter;
    }

    private static TimeSpan ClampWait(double seconds) {
        if (double.IsNaN(seconds) || seconds <= 0) {
            return DefaultRetryAfter;
        }
        return seconds >= MaxRetryAfter.TotalSeconds ? MaxRetryAfter : TimeSpan.FromSeconds(seconds);
    }

    private Uri BuildRequestUri(double? after, int offset) {
        var query = new StringBuilder(_endpoint);
        query.Append("?limit=").Append(PageLimit.ToString(CultureInfo.InvariantCulture));
        if (after is { } value) {
            query.Append("&after=").Append(value.ToString("R", CultureInfo.InvariantCulture));
        }
        if (offset > 0) {
            query.Append("&offset=").Append(offset.ToString(CultureInfo.InvariantCulture));
        }
        return new Uri(query.ToString(), UriKind.Absolute);
    }

    private GameEventsResponse? Parse(ReadOnlySpan<byte> utf8Json) {
        try {
            return JsonSerializer.Deserialize<GameEventsResponse>(utf8Json, Json);
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "game events payload from {BaseUrl} was not valid json", _baseUrl);
            return null;
        }
    }

    private bool Merge(IReadOnlyList<GameEvent> incoming) {
        bool changed = false;
        lock (_gate) {
            foreach (var candidate in incoming) {
                if (string.IsNullOrEmpty(candidate.Id)) {
                    continue;
                }
                var key = new GameEventKey(candidate.Id, candidate.StartTimestamp);
                if (_events.TryGetValue(key, out var existing)) {
                    if (existing.IsDeviceSourced && !candidate.IsDeviceSourced) {
                        continue;
                    }
                    if (existing == candidate) {
                        continue;
                    }
                }
                _events[key] = candidate;
                changed = true;
            }
        }
        return changed;
    }

    private async Task SaveAsync(CancellationToken cancellationToken) {
        if (_store is null) {
            return;
        }
        var snapshot = Snapshot();
        try {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new GameEventsResponse { Total = snapshot.Count, Events = snapshot }, Json);
            await _store.SaveAsync(payload, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            _logger?.LogWarning(ex, "game events store write failed");
        }
    }

    private static int NewestFirst(GameEvent left, GameEvent right) {
        int byStart = right.StartTimestamp.CompareTo(left.StartTimestamp);
        return byStart != 0 ? byStart : string.CompareOrdinal(right.Id, left.Id);
    }
}
