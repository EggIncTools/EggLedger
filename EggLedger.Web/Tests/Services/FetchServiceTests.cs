using System.IO.Compression;
using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.Tests.Data;
using Ei;
using ProtoBuf;

namespace EggLedger.Web.Tests.Services;

public sealed class FetchServiceTests {
    private const string Eid = "EI1234567890123456";

    private static FetchService Make(
        FakeIndexedDb db,
        HttpMessageHandler handler,
        int workerCount = 4,
        IndexedDbSettings? settings = null) {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var api = new ApiClient(http);
        settings ??= new IndexedDbSettings(db);
        if (workerCount != 1) {
            settings.SetSettingAsync("worker_count", workerCount.ToString()).GetAwaiter().GetResult();
        }
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var accounts = new IndexedDbAccountStore(settings);
        return new FetchService(api, store, settings, accounts, new LocalApiPayloadDecoder(api));
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


    private sealed class RoutingHandler : HttpMessageHandler {
        private readonly Func<string, string?>? _firstContact;
        private readonly Func<string, string?> _completeMission;
        public int FirstContactHits;
        public readonly List<string> CompleteMissionRequests = [];

        public RoutingHandler(string firstContactBody, Func<string, string?> completeMission) {
            _firstContact = _ => firstContactBody;
            _completeMission = completeMission;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            string path = request.RequestUri!.AbsolutePath;
            string form = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith(ApiClient.FirstContactEndpoint, StringComparison.Ordinal)) {
                Interlocked.Increment(ref FirstContactHits);
                return Ok(_firstContact!(form));
            }
            if (path.EndsWith(ApiClient.CompleteMissionEndpoint, StringComparison.Ordinal)) {
                string id = ExtractMissionId(form);
                lock (CompleteMissionRequests) {
                    CompleteMissionRequests.Add(id);
                }
                string? body = _completeMission(id);
                if (body is null) {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) {
                        Content = new StringContent(""),
                    };
                }
                return Ok(body);
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
        var service = Make(db, handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FetchPlayerDataAsync(Eid, null, CancellationToken.None));

        Assert.Contains("please double check your ID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchPlayerData_StoresFreshMission_RoundTripsViaStore() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(new[] { "m1" }), CompleteMissionBody);
        var service = Make(db, handler);

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
        var service = Make(db, handler);

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
        await Make(db, handler).FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        var inFlight = await store.GetInFlightMissionsAsync(Eid);
        Assert.Equal("flying", Assert.Single(inFlight).MissiondId);
    }

    [Fact]
    public async Task FetchPlayerData_WritesArtifactDropRows() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(new[] { "m1" }), CompleteMissionBody);
        var service = Make(db, handler);

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
        var handler = new RoutingHandler(FirstContactBody(new[] { "m1" }), CompleteMissionBody);
        var service = Make(db, handler);

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

        var handler = new RoutingHandler(FirstContactBody(Array.Empty<string>(), soulEggs: 10_800_000_000_000), CompleteMissionBody);
        var service = Make(db, handler, settings: settings);

        await service.FetchPlayerDataAsync(Eid, null, CancellationToken.None);

        var updated = (await accounts.GetKnownAccountsAsync()).Single(a => a.Id == Eid);
        Assert.Equal("10.8T", updated.SeString);
    }

    [Fact]
    public async Task FetchPlayerData_InsertsBackup() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(Array.Empty<string>(), lastBackupTime: 5000), CompleteMissionBody);
        var service = Make(db, handler);

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
        var service = Make(db, handler, workerCount: 5);

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
        var handler = new RoutingHandler(FirstContactBody(new[] { "m1" }), CompleteMissionBody);
        var service = Make(db, handler, workerCount: 1);

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
            FirstContactBody(new[] { "bad" }),
            id => id == "bad" ? null : CompleteMissionBody(id));
        var service = Make(db, handler);

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
            FirstContactBody(new[] { "flaky" }),
            id => {
                if (id == "flaky" && Interlocked.Increment(ref attempts) == 1) {
                    return null;
                }
                return CompleteMissionBody(id);
            });
        var service = Make(db, handler);

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
        var service = Make(db, handler, workerCount: 2);

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


    private sealed class SynchronousProgress<T> : IProgress<T> {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
