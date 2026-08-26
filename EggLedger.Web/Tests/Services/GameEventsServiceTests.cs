using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EggLedger.Web.Services;

namespace EggLedger.Web.Tests.Services;

public sealed class GameEventsServiceTests {
    private const string BaseUrl = "https://egi.test";
    private const string ApiKey = "egi_secret_value";

    private const string DocumentedBody = """
    { "total": 1, "events": [
      { "id": "piggy-cap-boost", "type": "piggy-boost", "message": "Increased Piggy Bank capacity!",
        "multiplier": 2.0, "ultra": false,
        "startTimestamp": 1756000000.0, "endTimestamp": 1756172800.0, "source": "device" } ] }
    """;

    private const string ExtendedBody = """
    { "total": 1, "generatedAt": "2026-08-25T00:00:00Z", "events": [
      { "id": "a", "type": "t", "message": "m", "multiplier": 1.5, "ultra": true,
        "startTimestamp": 100, "endTimestamp": 200, "source": "carpet",
        "futureField": { "nested": [1, 2, 3] }, "anotherOne": 7 } ] }
    """;

    private sealed class TestClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class FakeStore : IGameEventStore {
        public byte[]? Data { get; set; }
        public int Loads { get; private set; }
        public int Saves { get; private set; }

        public Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default) {
            Loads++;
            return Task.FromResult(Data);
        }

        public Task SaveAsync(byte[] utf8Json, CancellationToken cancellationToken = default) {
            Saves++;
            Data = utf8Json;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHandler : HttpMessageHandler {
        private readonly Queue<HttpResponseMessage> _queue = new();
        public List<string> Uris { get; } = [];
        public List<string?> Keys { get; } = [];
        public Exception? Fault { get; set; }
        public int Hits => Uris.Count;

        public FakeHandler Reply(string body, HttpStatusCode status = HttpStatusCode.OK) {
            _queue.Enqueue(new HttpResponseMessage(status) {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
            return this;
        }

        public FakeHandler RateLimited(double retryAfterSeconds) {
            var seconds = retryAfterSeconds.ToString("R", CultureInfo.InvariantCulture);
            var body = $"{{\"error\":\"rate_limited\",\"retryAfterSeconds\":{seconds}}}";
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
            _queue.Enqueue(response);
            return this;
        }

        public FakeHandler RateLimitedHeaderOnly(double retryAfterSeconds) {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) {
                Content = new StringContent("{\"error\":\"rate_limited\"}", Encoding.UTF8, "application/json"),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
            _queue.Enqueue(response);
            return this;
        }

        public FakeHandler Unavailable() =>
            Reply("{\"error\":\"no database configured\"}", HttpStatusCode.ServiceUnavailable);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Uris.Add(request.RequestUri!.ToString());
            Keys.Add(request.Headers.TryGetValues(GameEventsService.ApiKeyHeader, out var values)
                ? string.Join(",", values)
                : null);
            if (Fault is not null) {
                throw Fault;
            }
            if (_queue.Count == 0) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("{\"total\":0,\"events\":[]}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(_queue.Dequeue());
        }
    }

    private static string EventJson(string id, double start, double end, string source = "device") {
        var startText = start.ToString("R", CultureInfo.InvariantCulture);
        var endText = end.ToString("R", CultureInfo.InvariantCulture);
        return $"{{\"id\":\"{id}\",\"type\":\"piggy-boost\",\"message\":\"m\",\"multiplier\":2.0,\"ultra\":false,"
            + $"\"startTimestamp\":{startText},\"endTimestamp\":{endText},\"source\":\"{source}\"}}";
    }

    private static string Body(int total, params string[] events) {
        var totalText = total.ToString(CultureInfo.InvariantCulture);
        return $"{{\"total\":{totalText},\"events\":[{string.Join(",", events)}]}}";
    }

    private static GameEventsService Make(
        FakeHandler handler,
        TestClock clock,
        string? baseUrl = BaseUrl,
        string? apiKey = null,
        IGameEventStore? store = null) =>
        new(new HttpClient(handler), baseUrl, apiKey, store, logger: null, clock: clock);

    [Fact]
    public async Task Refresh_ParsesDocumentedResponseShape() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, new TestClock());

        await service.RefreshAsync();

        Assert.Equal(1, handler.Hits);
        Assert.Equal(1, service.Count);
        var parsed = Assert.Single(service.Snapshot());
        Assert.Equal("piggy-cap-boost", parsed.Id);
        Assert.Equal("piggy-boost", parsed.Type);
        Assert.Equal("Increased Piggy Bank capacity!", parsed.Message);
        Assert.Equal(2.0, parsed.Multiplier);
        Assert.False(parsed.Ultra);
        Assert.Equal(1756000000.0, parsed.StartTimestamp);
        Assert.Equal(1756172800.0, parsed.EndTimestamp);
        Assert.Equal("device", parsed.Source);
        Assert.True(parsed.IsDeviceSourced);
    }

    [Fact]
    public async Task Refresh_IgnoresUnknownFields() {
        var handler = new FakeHandler().Reply(ExtendedBody);
        var service = Make(handler, new TestClock());

        await service.RefreshAsync();

        var parsed = Assert.Single(service.Snapshot());
        Assert.Equal("a", parsed.Id);
        Assert.True(parsed.Ultra);
        Assert.False(parsed.IsDeviceSourced);
    }

    [Fact]
    public async Task ActiveAt_CoversStartToEndWindow() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, new TestClock());
        await service.RefreshAsync();

        Assert.Single(service.ActiveAt(DateTimeOffset.FromUnixTimeSeconds(1756086400)));
        Assert.Single(service.ActiveAt(DateTimeOffset.FromUnixTimeSeconds(1756000000)));
        Assert.Single(service.ActiveAt(DateTimeOffset.FromUnixTimeSeconds(1756172800)));
        Assert.Empty(service.ActiveAt(DateTimeOffset.FromUnixTimeSeconds(1755999999)));
        Assert.Empty(service.ActiveAt(DateTimeOffset.FromUnixTimeSeconds(1756172801)));
    }

    [Fact]
    public async Task Merge_KeysOnIdAndStartTimestamp() {
        var handler = new FakeHandler()
            .Reply(Body(2, EventJson("sale", 1000, 2000), EventJson("sale", 500000, 501000)));
        var service = Make(handler, new TestClock());

        await service.RefreshAsync();

        Assert.Equal(2, service.Count);
    }

    [Fact]
    public async Task Merge_DeviceRowWinsOverCarpetRow() {
        var clock = new TestClock();
        var handler = new FakeHandler()
            .Reply(Body(1, EventJson("sale", 1000, 2000, source: "carpet")))
            .Reply(Body(1, EventJson("sale", 1000, 3000, source: "device")))
            .Reply(Body(1, EventJson("sale", 1000, 9999, source: "carpet")));
        var service = Make(handler, clock);

        await service.RefreshAsync();
        Assert.Equal(2000.0, Assert.Single(service.Snapshot()).EndTimestamp);

        clock.Advance(GameEventsService.RefreshFloor);
        await service.RefreshAsync();
        var afterDevice = Assert.Single(service.Snapshot());
        Assert.Equal(3000.0, afterDevice.EndTimestamp);
        Assert.True(afterDevice.IsDeviceSourced);

        clock.Advance(GameEventsService.RefreshFloor);
        await service.RefreshAsync();
        var afterCarpet = Assert.Single(service.Snapshot());
        Assert.Equal(3000.0, afterCarpet.EndTimestamp);
        Assert.True(afterCarpet.IsDeviceSourced);
    }

    [Fact]
    public async Task Refresh_WithinFloor_IsNoOp() {
        var clock = new TestClock();
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, clock);

        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(4));
        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(1));
        await service.RefreshAsync();
        Assert.Equal(2, handler.Hits);
    }

    [Fact]
    public async Task Refresh_ColdHasNoAfterFilter_WarmIsIncremental() {
        var clock = new TestClock();
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, clock);

        await service.RefreshAsync();
        Assert.DoesNotContain("after=", handler.Uris[0], StringComparison.Ordinal);
        Assert.Contains("limit=1000", handler.Uris[0], StringComparison.Ordinal);

        clock.Advance(GameEventsService.RefreshFloor);
        await service.RefreshAsync();

        double expected = 1756000000.0 - GameEventsService.MergeWindow.TotalSeconds;
        Assert.Contains(
            "after=" + expected.ToString("R", CultureInfo.InvariantCulture),
            handler.Uris[1],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_FutureStartTimestamp_DoesNotPoisonWarmCursor() {
        var clock = new TestClock();
        var poison = EventJson("poison", 4102444800, 4102531200);
        var store = new FakeStore { Data = Encoding.UTF8.GetBytes(Body(1, poison)) };
        var handler = new FakeHandler().Reply(Body(0));
        var service = Make(handler, clock, store: store);

        await service.EnsureLoadedAsync();
        Assert.Equal(0, handler.Hits);

        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);

        double nowSeconds = clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0;
        double cursor = AfterValue(handler.Uris[0]);
        Assert.True(cursor <= nowSeconds);
        Assert.Equal(nowSeconds - GameEventsService.MergeWindow.TotalSeconds, cursor);
    }

    private static double AfterValue(string uri) {
        const string marker = "after=";
        int start = uri.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "request had no after cursor: " + uri);
        var rest = uri[(start + marker.Length)..];
        int end = rest.IndexOf('&', StringComparison.Ordinal);
        var text = end < 0 ? rest : rest[..end];
        return double.Parse(text, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Refresh_WhenHandlerThrows_StillHonorsFloor() {
        var clock = new TestClock();
        var handler = new FakeHandler { Fault = new InvalidOperationException("handler exploded") };
        var service = Make(handler, clock);

        await Assert.ThrowsAnyAsync<Exception>(() => service.RefreshAsync());
        Assert.Equal(1, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(4));
        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(1));
        handler.Fault = null;
        handler.Reply(DocumentedBody);
        await service.RefreshAsync();
        Assert.Equal(2, handler.Hits);
        Assert.Equal(1, service.Count);
    }

    [Fact]
    public async Task Refresh_RateLimitHeaderIsClampedToOneHour() {
        var clock = new TestClock();
        var handler = new FakeHandler().RateLimitedHeaderOnly(999999);
        var service = Make(handler, clock);

        await service.RefreshAsync();

        Assert.Equal(clock.GetUtcNow().AddHours(1), service.NextAllowedRefresh);
    }

    [Fact]
    public async Task Refresh_PagesOnlyWhenTotalExceedsPage() {
        var handler = new FakeHandler()
            .Reply(Body(3, EventJson("a", 100, 200), EventJson("b", 300, 400)))
            .Reply(Body(3, EventJson("c", 500, 600)));
        var service = Make(handler, new TestClock());

        await service.RefreshAsync();

        Assert.Equal(2, handler.Hits);
        Assert.DoesNotContain("offset=", handler.Uris[0], StringComparison.Ordinal);
        Assert.Contains("offset=1000", handler.Uris[1], StringComparison.Ordinal);
        Assert.Equal(3, service.Count);
    }

    [Fact]
    public async Task Refresh_RateLimited_GatesUntilRetryAfterElapses() {
        var clock = new TestClock();
        var handler = new FakeHandler()
            .RateLimited(900)
            .Reply(DocumentedBody);
        var service = Make(handler, clock);

        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);
        Assert.Equal(0, service.Count);

        clock.Advance(TimeSpan.FromMinutes(10));
        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(6));
        await service.RefreshAsync();
        Assert.Equal(2, handler.Hits);
        Assert.Equal(1, service.Count);
    }

    [Fact]
    public async Task Refresh_Unavailable_BacksOffBeyondFloor() {
        var clock = new TestClock();
        var handler = new FakeHandler()
            .Unavailable()
            .Unavailable()
            .Reply(DocumentedBody);
        var service = Make(handler, clock);

        await service.RefreshAsync();
        Assert.Equal(1, handler.Hits);

        clock.Advance(GameEventsService.RefreshFloor);
        await service.RefreshAsync();
        Assert.Equal(2, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(6));
        await service.RefreshAsync();
        Assert.Equal(2, handler.Hits);

        clock.Advance(TimeSpan.FromMinutes(5));
        await service.RefreshAsync();
        Assert.Equal(3, handler.Hits);
        Assert.Equal(1, service.Count);
    }

    [Fact]
    public async Task Unconfigured_MakesNoHttpCallsAndReturnsNothing() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var store = new FakeStore();
        var service = Make(handler, new TestClock(), baseUrl: null, apiKey: ApiKey, store: store);

        Assert.False(service.IsConfigured);
        await service.EnsureLoadedAsync();
        await service.RefreshAsync();
        await service.RefreshAsync();

        Assert.Equal(0, handler.Hits);
        Assert.Equal(0, store.Loads);
        Assert.Equal(0, store.Saves);
        Assert.False(service.HasData);
        Assert.Empty(service.ActiveAt(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task Unconfigured_WhenBaseUrlIsBlank_StaysInert() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, new TestClock(), baseUrl: "   ");

        await service.EnsureLoadedAsync();
        await service.RefreshAsync();

        Assert.False(service.IsConfigured);
        Assert.Equal(0, handler.Hits);
    }

    [Fact]
    public async Task EnsureLoaded_ReadsStoreBeforeNetwork_AndRefreshSaves() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var store = new FakeStore();
        var first = Make(handler, new TestClock(), store: store);

        await first.EnsureLoadedAsync();
        Assert.Equal(1, handler.Hits);
        Assert.Equal(1, store.Loads);
        Assert.Equal(1, store.Saves);
        Assert.NotNull(store.Data);

        var replayHandler = new FakeHandler();
        var second = Make(replayHandler, new TestClock(), store: store);
        await second.EnsureLoadedAsync();

        Assert.Equal(0, replayHandler.Hits);
        Assert.Equal(1, second.Count);
        Assert.Equal("piggy-cap-boost", Assert.Single(second.Snapshot()).Id);
    }

    [Fact]
    public async Task EnsureLoaded_RunsOnlyOnce() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, new TestClock());

        await service.EnsureLoadedAsync();
        await service.EnsureLoadedAsync();

        Assert.Equal(1, handler.Hits);
    }

    [Fact]
    public async Task ApiKey_SentAsHeaderOnlyWhenConfigured() {
        var withKeyHandler = new FakeHandler().Reply(DocumentedBody);
        var withKey = Make(withKeyHandler, new TestClock(), apiKey: ApiKey);
        await withKey.RefreshAsync();

        Assert.Equal(ApiKey, withKeyHandler.Keys[0]);
        Assert.DoesNotContain(ApiKey, withKeyHandler.Uris[0], StringComparison.Ordinal);

        var noKeyHandler = new FakeHandler().Reply(DocumentedBody);
        var noKey = Make(noKeyHandler, new TestClock());
        await noKey.RefreshAsync();

        Assert.Null(noKeyHandler.Keys[0]);
    }

    [Fact]
    public async Task ApiKey_BlankValueIsTreatedAsAbsent() {
        var handler = new FakeHandler().Reply(DocumentedBody);
        var service = Make(handler, new TestClock(), apiKey: "  ");

        await service.RefreshAsync();

        Assert.Null(handler.Keys[0]);
    }
}
