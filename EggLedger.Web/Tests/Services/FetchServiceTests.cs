using System.IO.Compression;
using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.Tests.Data;
using Ei;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoBuf;

namespace EggLedger.Web.Tests.Services;

public sealed class FetchServiceTests {
    private const string Eid = "EI1234567890123456";

    private static async Task<FetchService> MakeAsync(
        FakeIndexedDb db,
        HttpMessageHandler handler,
        int workerCount = 4,
        IndexedDbSettings? settings = null,
        IAutoExporter? exporter = null) {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var api = new ApiClient(http);
        settings ??= new IndexedDbSettings(db);
        if (workerCount != 1) {
            await settings.SetSettingAsync("worker_count", workerCount.ToString());
        }
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var accounts = new IndexedDbAccountStore(settings);
        return new FetchService(api, store, settings, accounts, new LocalApiPayloadDecoder(api), NullLogger<FetchService>.Instance, autoExporter: exporter);
    }

    private static string ToApiBody<T>(T msg) {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string FirstContactBody(IEnumerable<string> completedMissionIds, double lastBackupTime = 1000, double soulEggs = 0) {
        var afxdb = new ArtifactsDB();
        foreach (var id in completedMissionIds) {
            afxdb.MissionInfos.Add(new MissionInfo {
                Identifier = id,
                status = MissionInfo.Status.Complete,
                StartTimeDerived = 500,
            });
        }
        var fc = new EggIncFirstContactResponse {
            Backup = new Backup {
                UserName = "tester",
                game = new Backup.Game { SoulEggsD = soulEggs },
                settings = new Backup.Settings { LastBackupTime = lastBackupTime },
                ArtifactsDb = afxdb,
            },
        };
        return ToApiBody(fc);
    }


    private static string InProgressFirstContactBody(params (string Id, MissionInfo.Status Status)[] inProgress) {
        var afxdb = new ArtifactsDB();
        foreach (var (id, status) in inProgress) {
            afxdb.MissionInfos.Add(new MissionInfo {
                Identifier = id,
                status = status,
                StartTimeDerived = 500,
                DurationSeconds = 3600,
                Ship = MissionInfo.Spaceship.Henerprise,
                duration_type = MissionInfo.DurationType.Epic,
            });
        }
        var fc = new EggIncFirstContactResponse {
            Backup = new Backup {
                UserName = "tester",
                game = new Backup.Game { SoulEggsD = 0 },
                settings = new Backup.Settings { LastBackupTime = 1000 },
                ArtifactsDb = afxdb,
            },
        };
        return ToApiBody(fc);
    }


    private static string InvalidFirstContactBody() {
        var fc = new EggIncFirstContactResponse();
        return ToApiBody(fc);
    }

    private static CompleteMissionResponse MissionResponse(string id) {
        var resp = new CompleteMissionResponse {
            Success = true,
            Info = new MissionInfo {
                Identifier = id,
                Ship = MissionInfo.Spaceship.Henerprise,
                StartTimeDerived = 500,
            },
        };
        resp.Artifacts.Add(new CompleteMissionResponse.SecureArtifactSpec {
            Spec = new ArtifactSpec { name = ArtifactSpec.Name.TachyonDeflector },
        });
        return resp;
    }

    private static string CompleteMissionBody(string id) {
        using var inner = new MemoryStream();
        Serializer.Serialize(inner, MissionResponse(id));
        var auth = new AuthenticatedMessage { Message = inner.ToArray(), Compressed = false };
        using var authBytes = new MemoryStream();
        Serializer.Serialize(authBytes, auth);
        return Convert.ToBase64String(authBytes.ToArray());
    }


    private sealed class RoutingHandler(string firstContactBody, Func<string, string?> completeMission) : HttpMessageHandler {
        public int FirstContactHits;
        public readonly List<string> CompleteMissionRequests = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            string path = request.RequestUri!.AbsolutePath;
            string form = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith(ApiClient.FirstContactEndpoint, StringComparison.Ordinal)) {
                Interlocked.Increment(ref FirstContactHits);
                return Ok(firstContactBody);
            }
            if (path.EndsWith(ApiClient.CompleteMissionEndpoint, StringComparison.Ordinal)) {
                string id = ExtractMissionId(form);
                lock (CompleteMissionRequests) {
                    CompleteMissionRequests.Add(id);
                }
                return completeMission(id) is { } body
                    ? Ok(body)
                    : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("") };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Ok(string? body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body ?? "") };


        private static string ExtractMissionId(string form) {
            const string prefix = "data=";
            int i = form.IndexOf(prefix, StringComparison.Ordinal);
            string enc = i >= 0 ? form[(i + prefix.Length)..] : form;
            byte[] bytes = Convert.FromBase64String(Uri.UnescapeDataString(enc));
            using var ms = new MemoryStream(bytes);
            var req = Serializer.Deserialize<MissionRequest>(ms);
            return req.Info!.Identifier;
        }
    }

    [Fact]
    public async Task FetchPlayerData_InvalidEid_Throws_DoubleCheckYourId() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(InvalidFirstContactBody(), _ => CompleteMissionBody("x"));
        var service = await MakeAsync(db, handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FetchPlayerDataAsync(Eid, null, CancellationToken.None));

        Assert.Contains("please double check your ID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchPlayerData_StoresFreshMission_RoundTripsViaStore() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var service = await MakeAsync(db, handler);

        var final = await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        Assert.Equal(AppState.Success, final);
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var got = await store.GetCompleteMissionAsync(Eid, "m1");
        Assert.NotNull(got);
        Assert.True(got!.Success);
        Assert.Equal("m1", got.Info!.Identifier);
        Assert.Equal(MissionInfo.Spaceship.Henerprise, got.Info.Ship);
        Assert.Single(got.Artifacts);
        Assert.Equal(ArtifactSpec.Name.TachyonDeflector, got.Artifacts[0].Spec!.name);
    }

    [Fact]
    public async Task FetchPlayerData_PersistsAllInProgressInFlightMissions() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(
            InProgressFirstContactBody(
                ("flying", MissionInfo.Status.Exploring),
                ("fueling", MissionInfo.Status.Fueling)),
            CompleteMissionBody);
        var service = await MakeAsync(db, handler);

        var final = await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        Assert.Equal(AppState.Success, final);
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var inFlight = await store.GetInFlightMissionsAsync(Eid);
        var ids = inFlight.Select(m => m.MissiondId).ToList();
        Assert.Contains("flying", ids);
        Assert.Contains("fueling", ids);
    }

    [Fact]
    public async Task FetchPlayerData_ReplacesThePreviousInFlightSet() {
        var db = new FakeIndexedDb();
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        await store.ReplaceInFlightMissionsAsync(Eid, [new DatabaseMission { MissiondId = "landed", LaunchDT = 1 }]);

        var handler = new RoutingHandler(
            InProgressFirstContactBody(("flying", MissionInfo.Status.Exploring)), CompleteMissionBody);
        var service = await MakeAsync(db, handler);
        await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        var inFlight = await store.GetInFlightMissionsAsync(Eid);
        Assert.Equal("flying", Assert.Single(inFlight).MissiondId);
    }

    [Fact]
    public async Task FetchPlayerData_WritesArtifactDropRows() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var service = await MakeAsync(db, handler);

        await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        var drops = await db.GetAllAsync<ArtifactDropRow>("artifact_drops");
        var drop = Assert.Single(drops);
        Assert.Equal("m1", drop.MissionId);
        Assert.Equal(Eid, drop.PlayerId);
        Assert.Equal(0, drop.DropIndex);
        Assert.Equal((int)ArtifactSpec.Name.TachyonDeflector, drop.ArtifactId);
        Assert.Equal("Artifact", drop.SpecType);
    }

    [Fact]
    public async Task FetchPlayerData_CachedMission_NotRefetched() {
        var db = new FakeIndexedDb();

        db.Seed("mission", SeededMission("m1"));
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var service = await MakeAsync(db, handler);

        var final = await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        Assert.Equal(AppState.Success, final);
        Assert.Empty(handler.CompleteMissionRequests);
    }

    [Fact]
    public async Task FetchPlayerData_RefreshesAccountStatsFromLatestBackup() {
        var db = new FakeIndexedDb();
        var settings = new IndexedDbSettings(db);
        var accounts = new IndexedDbAccountStore(settings);
        await accounts.AddKnownAccountAsync(new EggLedger.Domain.MissionQuery.AccountInfo {
            Id = Eid,
            Nickname = "tester",
            EBString = "0",
            AccountColor = "abc",
            SeString = "0",
        });

        var handler = new RoutingHandler(FirstContactBody([], soulEggs: 10_800_000_000_000), CompleteMissionBody);
        var service = await MakeAsync(db, handler, settings: settings);

        await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        var updated = (await accounts.GetKnownAccountsAsync()).Single(a => a.Id == Eid);
        Assert.Equal("10.8T", updated.SeString);
    }

    [Fact]
    public async Task FetchPlayerData_InsertsBackup() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody([], lastBackupTime: 5000), CompleteMissionBody);
        var service = await MakeAsync(db, handler);

        await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        var backups = await db.GetAllAsync<BackupRow>("backup");
        var backup = Assert.Single(backups);
        Assert.Equal(Eid, backup.PlayerId);
        Assert.Equal(5000, backup.RecordedAt);
        Assert.NotEmpty(backup.Payload);
    }

    [Fact]
    public async Task FetchPlayerData_FansOutManyMissions() {
        var db = new FakeIndexedDb();
        var ids = Enumerable.Range(0, 25).Select(i => $"m{i}").ToArray();
        var handler = new RoutingHandler(FirstContactBody(ids), CompleteMissionBody);
        var service = await MakeAsync(db, handler, workerCount: 5);

        var final = await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        Assert.Equal(AppState.Success, final);
        Assert.Equal(25, handler.CompleteMissionRequests.Count);
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var stored = await store.GetCompleteMissionIdsAsync(Eid);
        Assert.Equal(25, stored!.Count);
    }

    [Fact]
    public async Task FetchPlayerData_ReportsStateAndSegmentSequence() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var service = await MakeAsync(db, handler, workerCount: 1);

        var events = new List<FetchProgress>();
        var progress = new SynchronousProgress<FetchProgress>(events.Add);

        await service.FetchPlayerDataAsync(Eid, progress, CancellationToken.None);

        var states = events.Select(e => e.State).Distinct().ToList();
        Assert.Contains(AppState.FetchingSave, states);
        Assert.Contains(AppState.FetchingMissions, states);
        Assert.Contains(AppState.ExportingData, states);
        Assert.Contains(AppState.Success, states);

        var segs = events
            .Where(e => e.MissionId == "m1" && e.Segment is not null)
            .Select(e => (e.Segment, e.SegmentStatus))
            .ToList();
        Assert.Contains(("Cache", (SegmentStatus?)SegmentStatus.Active), segs);
        Assert.Contains(("Fetch", (SegmentStatus?)SegmentStatus.Done), segs);
        Assert.Contains(("Decode", (SegmentStatus?)SegmentStatus.Done), segs);
        Assert.Contains(("Store", (SegmentStatus?)SegmentStatus.Done), segs);
    }

    [Fact]
    public async Task FetchPlayerData_MissionFails_ReportsFailedMissionWithReason() {
        var db = new FakeIndexedDb();

        var handler = new RoutingHandler(
            FirstContactBody(["bad"]),
            id => id == "bad" ? null : CompleteMissionBody(id));
        var service = await MakeAsync(db, handler);

        var events = new List<FetchProgress>();
        var progress = new SynchronousProgress<FetchProgress>(events.Add);

        var final = await service.FetchPlayerDataAsync(Eid, progress, CancellationToken.None);

        Assert.Equal(AppState.Failed, final);
        var failedReport = events.Last(e => e.State == AppState.Failed);
        Assert.Equal(5, failedReport.Retried);
        var failure = Assert.Single(failedReport.FailedMissions);
        Assert.Equal("bad", failure.MissionId);
        Assert.NotEmpty(failure.Reason);
        Assert.False(failure.IsTimeout);
    }

    [Fact]
    public async Task FetchPlayerData_TransientFailure_RecoversViaAutomaticRetry() {
        var db = new FakeIndexedDb();
        int attempts = 0;
        var handler = new RoutingHandler(
            FirstContactBody(["flaky"]),
            id => id == "flaky" && Interlocked.Increment(ref attempts) == 1 ? null : CompleteMissionBody(id));
        var service = await MakeAsync(db, handler);

        var final = await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        Assert.Equal(AppState.Success, final);
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        Assert.NotNull(await store.GetCompleteMissionAsync(Eid, "flaky"));
    }

    [Theory]
    [InlineData("POST https://x: timeout after 00:00:05", true)]
    [InlineData("POST https://x: HTTP 500: ", false)]
    public void FailedMission_IsTimeout_DetectsTimeoutReason(string reason, bool expected) {
        var failure = new FailedMission("m1", 0, reason);
        Assert.Equal(expected, failure.IsTimeout);
    }

    [Fact]
    public async Task FetchPlayerData_Cancellation_YieldsInterrupted() {
        var db = new FakeIndexedDb();
        using var cts = new CancellationTokenSource();
        var ids = Enumerable.Range(0, 20).Select(i => $"m{i}").ToArray();

        var handler = new RoutingHandler(FirstContactBody(ids), id => {
            cts.Cancel();
            return CompleteMissionBody(id);
        });
        var service = await MakeAsync(db, handler, workerCount: 2);

        var final = await service.FetchPlayerDataAsync(Eid, null, cts.Token);

        Assert.Equal(AppState.Interrupted, final);
    }

    private static MissionRow SeededMission(string id) {
        using var inner = new MemoryStream();
        Serializer.Serialize(inner, MissionResponse(id));
        var auth = new AuthenticatedMessage { Message = inner.ToArray(), Compressed = false };
        using var authBytes = new MemoryStream();
        Serializer.Serialize(authBytes, auth);
        using var gzipped = new MemoryStream();
        using (var gz = new GZipStream(gzipped, CompressionMode.Compress, leaveOpen: true)) {
            var raw = authBytes.ToArray();
            gz.Write(raw, 0, raw.Length);
        }
        return new MissionRow {
            PlayerId = Eid,
            MissionId = id,
            StartTimestamp = 500,
            Ship = 0,
            CompletePayload = gzipped.ToArray(),
        };
    }


    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T> {
        public void Report(T value) => handler(value);
    }

    private sealed class StubExporter(Func<IReadOnlyList<string>> run) : IAutoExporter {
        public Task<IReadOnlyList<string>> RunAfterFetchAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(run());
    }

    private static List<string> GlobalLog(List<FetchProgress> events) =>
        [.. events.Where(e => e.LogText is not null && !e.LogRowOnly).Select(e => e.LogText!)];

    [Fact]
    public async Task FetchPlayerData_EmitsSaveAndFoundAndDoneLines() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1", "m2"]), CompleteMissionBody);
        var service = await MakeAsync(db, handler);
        var events = new List<FetchProgress>();

        await service.FetchPlayerDataAsync(Eid, new SynchronousProgress<FetchProgress>(events.Add), CancellationToken.None);

        var log = GlobalLog(events);
        Assert.StartsWith($"successfully fetched backup for &7a7a7a<{Eid}> (&", log[0]);
        Assert.EndsWith("<tester>)", log[0]);
        Assert.DoesNotContain("TE>", log[1]);
        Assert.Contains("[img:soul_egg.png]", log[1]);
        Assert.StartsWith("updated local database EB to &", log[2]);
        Assert.StartsWith("backup is from &7a7a7a<", log[3]);
        Assert.Contains("found &148c32<2 completed> missions, &148c32<0 in-progress> missions, &148c32<2 to fetch>", log);
        Assert.Contains("successfully fetched &148c32<2 missions>", log);
        Assert.Equal("done.", log[^1]);
    }

    [Fact]
    public async Task FetchPlayerData_SegmentReportsCarryMissionLabel() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var service = await MakeAsync(db, handler, workerCount: 1);
        var events = new List<FetchProgress>();

        await service.FetchPlayerDataAsync(Eid, new SynchronousProgress<FetchProgress>(events.Add), CancellationToken.None);

        var segments = events.Where(e => e.Segment is not null).ToList();
        Assert.NotEmpty(segments);
        var expected = FetchService.MissionLabel(new MissionInfo { StartTimeDerived = 500 });
        Assert.EndsWith(", 1970-01-01", expected);
        Assert.All(segments, e => Assert.Equal(expected, e.MissionLabel));
    }

    [Fact]
    public async Task FetchPlayerData_MissionFails_RoutesRetriesRowOnlyAndFinalErrorGlobally() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["bad"]), _ => null);
        var service = await MakeAsync(db, handler);
        var events = new List<FetchProgress>();

        await service.FetchPlayerDataAsync(Eid, new SynchronousProgress<FetchProgress>(events.Add), CancellationToken.None);

        var retries = events.Where(e => e.LogRowOnly).ToList();
        Assert.Equal(5, retries.Count);
        Assert.All(retries, e => Assert.Equal("bad", e.MissionId));
        Assert.StartsWith("attempt 1 failed: ", retries[0].LogText);
        Assert.EndsWith("retrying in 0.5s", retries[0].LogText);

        var finalError = Assert.Single(events, e => e is { LogIsError: true, LogRowOnly: false, MissionId: "bad" });
        Assert.NotEmpty(finalError.LogText!);

        var log = GlobalLog(events);
        Assert.Contains("1 of 1 missions failed to fetch", log);
        Assert.Equal("(performing another &7a7a7a<fetch> will fetch the failed missions most of the time)", log[^1]);
    }

    [Fact]
    public async Task FetchPlayerData_RunsExporterBeforeSuccess_AndReportsFiles() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var events = new List<FetchProgress>();
        AppState? stateAtExport = null;
        var exporter = new StubExporter(() => {
            stateAtExport = events[^1].State;
            return ["a.csv"];
        });
        var service = await MakeAsync(db, handler, exporter: exporter);

        var final = await service.FetchPlayerDataAsync(Eid, new SynchronousProgress<FetchProgress>(events.Add), CancellationToken.None);

        Assert.Equal(AppState.Success, final);
        Assert.Equal(AppState.ExportingData, stateAtExport);
        Assert.Equal(["a.csv"], events[^1].ExportedFiles);
    }

    [Fact]
    public async Task FetchPlayerData_ExporterThrows_YieldsFailedWithError() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var exporter = new StubExporter(() => throw new IOException("disk full"));
        var service = await MakeAsync(db, handler, exporter: exporter);
        var events = new List<FetchProgress>();

        var final = await service.FetchPlayerDataAsync(Eid, new SynchronousProgress<FetchProgress>(events.Add), CancellationToken.None);

        Assert.Equal(AppState.Failed, final);
        Assert.Contains(events, e => e is { LogText: "disk full", LogIsError: true });
        Assert.DoesNotContain("done.", GlobalLog(events));
    }

    [Fact]
    public async Task FetchPlayerData_InvalidEid_EmitsErrorLine() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(InvalidFirstContactBody(), CompleteMissionBody);
        var service = await MakeAsync(db, handler);
        var events = new List<FetchProgress>();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.FetchPlayerDataAsync(Eid, new SynchronousProgress<FetchProgress>(events.Add), CancellationToken.None));

        Assert.Contains(events, e => e.LogIsError && e.LogText!.Contains("please double check your ID", StringComparison.Ordinal));
    }
}
