using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.State;
using EggLedger.Web.Tests.Data;
using Ei;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoBuf;

namespace EggLedger.Web.Tests.Services;

public sealed class FetchOrchestratorTests {
    private const string Eid = "EI1234567890123456";

    private static FetchOrchestrator Make(FakeIndexedDb db, HttpMessageHandler handler) {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var api = new ApiClient(http);
        var settings = new IndexedDbSettings(db);
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var accounts = new IndexedDbAccountStore(settings);
        var fetch = new FetchService(api, store, settings, accounts, new LocalApiPayloadDecoder(api), NullLogger<FetchService>.Instance);
        return new FetchOrchestrator(fetch, new AppStateService(), settings, NullLogger<FetchOrchestrator>.Instance);
    }

    private static string ToApiBody<T>(T msg) {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string FirstContactBody(IEnumerable<string> completedMissionIds) {
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
                game = new Backup.Game(),
                settings = new Backup.Settings { LastBackupTime = 1000 },
                ArtifactsDb = afxdb,
            },
        };
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

    private sealed class RoutingHandler(string firstContactBody, Func<string, string?> completeMission, Action<string>? onCompleteMissionRequest = null, Action<string, CancellationToken>? onCompleteMissionRequestWithToken = null) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            string path = request.RequestUri!.AbsolutePath;
            string form = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith(ApiClient.FirstContactEndpoint, StringComparison.Ordinal)) {
                return Ok(firstContactBody);
            }
            if (path.EndsWith(ApiClient.CompleteMissionEndpoint, StringComparison.Ordinal)) {
                string id = ExtractMissionId(form);
                onCompleteMissionRequest?.Invoke(id);
                onCompleteMissionRequestWithToken?.Invoke(id, cancellationToken);
                return completeMission(id) is { } body
                    ? Ok(body)
                    : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("") };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Ok(string body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };

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
    public async Task StartFetchAsync_SetsFetchingAccountId() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var orchestrator = Make(db, handler);

        var task = orchestrator.StartFetchAsync(Eid);

        Assert.Equal(Eid, orchestrator.FetchingAccountId);
        await task;
    }

    private static FetchOrchestrator MakeDetached(TimeProvider clock) {
        var db = new FakeIndexedDb();
        var http = new HttpClient(new RoutingHandler("", _ => null)) { BaseAddress = new Uri("https://example.test") };
        var api = new ApiClient(http);
        var settings = new IndexedDbSettings(db);
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));
        var fetch = new FetchService(api, store, settings, new IndexedDbAccountStore(settings), new LocalApiPayloadDecoder(api), NullLogger<FetchService>.Instance);
        return new FetchOrchestrator(fetch, new AppStateService(), settings, NullLogger<FetchOrchestrator>.Instance, clock);
    }

    private static FetchProgress Segment(string id, string segment, SegmentStatus status) => new() {
        State = AppState.FetchingMissions,
        MissionId = id,
        MissionLabel = "Henerprise, Epic, 2026-09-01",
        Segment = segment,
        SegmentStatus = status,
    };

    [Fact]
    public void Apply_SegmentOnlyReport_CarriesForwardCounts() {
        using var orchestrator = MakeDetached(new ManualClock());

        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, Total = 7, Finished = 2, Failed = 1, Retried = 3 });
        orchestrator.Apply(Segment("m1", "Fetch", SegmentStatus.Active));

        var p = orchestrator.Progress!;
        Assert.Equal("Fetch", p.Segment);
        Assert.Equal((7, 2, 1, 3), (p.Total, p.Finished, p.Failed, p.Retried));
    }

    [Fact]
    public void Apply_ManyReports_CoalesceIntoOneChangedPerFlush() {
        using var orchestrator = MakeDetached(new ManualClock());
        int changed = 0;
        orchestrator.Changed += () => changed++;

        for (int i = 0; i < 100; i++) {
            orchestrator.Apply(Segment($"m{i % 3}", "Fetch", SegmentStatus.Active));
        }
        Assert.Equal(0, changed);

        orchestrator.Flush();
        Assert.Equal(1, changed);

        orchestrator.Apply(new FetchProgress { State = AppState.Failed });
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Apply_LogLines_RouteToGlobalAndRow() {
        using var orchestrator = MakeDetached(new ManualClock());

        orchestrator.Apply(new FetchProgress { State = AppState.FetchingSave, LogText = "hello" });
        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, MissionId = "m1", MissionLabel = "L", LogText = "retry", LogIsError = true, LogRowOnly = true });
        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, MissionId = "m1", MissionLabel = "L", LogText = "boom", LogIsError = true });
        orchestrator.Flush();

        Assert.Equal(["hello", "boom"], orchestrator.Log.Select(e => e.Text));
        var row = Assert.Single(orchestrator.Processes);
        Assert.Equal("L", row.Label);
        Assert.Equal(["retry", "boom"], row.Logs.Select(e => e.Text));
        Assert.Equal(ProcessStatus.Failed, row.Status);
    }

    [Fact]
    public void Apply_GlobalLog_CapsAt2000() {
        using var orchestrator = MakeDetached(new ManualClock());

        for (int i = 0; i < 2100; i++) {
            orchestrator.Apply(new FetchProgress { State = AppState.FetchingSave, LogText = $"line {i}" });
        }
        orchestrator.Flush();

        Assert.Equal(2000, orchestrator.Log.Count);
        Assert.Equal("line 100", orchestrator.Log[0].Text);
    }

    [Fact]
    public void Flush_ReapsFinishedRowsAfterFiveSeconds() {
        var clock = new ManualClock();
        using var orchestrator = MakeDetached(clock);

        orchestrator.Apply(Segment("m1", "Store", SegmentStatus.Done));
        orchestrator.Apply(Segment("m2", "Fetch", SegmentStatus.Active));
        orchestrator.Flush();
        Assert.Equal(2, orchestrator.Processes.Count);
        Assert.Equal(ProcessStatus.Done, orchestrator.Processes[0].Status);

        clock.Advance(TimeSpan.FromSeconds(5));
        orchestrator.Flush();

        Assert.Equal("m2", Assert.Single(orchestrator.Processes).Id);
    }

    [Fact]
    public void Flush_MoreThanFiveRows_HidesDoneRows() {
        using var orchestrator = MakeDetached(new ManualClock());

        for (int i = 0; i < 4; i++) {
            orchestrator.Apply(Segment($"r{i}", "Fetch", SegmentStatus.Active));
        }
        orchestrator.Apply(Segment("d0", "Cache", SegmentStatus.Done));
        orchestrator.Apply(Segment("d1", "Store", SegmentStatus.Done));
        orchestrator.Flush();

        Assert.Equal(4, orchestrator.Processes.Count);
        Assert.All(orchestrator.Processes, p => Assert.Equal(ProcessStatus.Running, p.Status));
    }

    [Fact]
    public void Apply_StageTransitions_NoMissionsNoExport_SkipsBoth() {
        using var orchestrator = MakeDetached(new ManualClock());

        orchestrator.Apply(new FetchProgress { State = AppState.FetchingSave });
        Assert.Equal(StageStatus.Active, orchestrator.SaveStage);

        orchestrator.Apply(new FetchProgress { State = AppState.ExportingData });
        Assert.Equal((StageStatus.Done, StageStatus.Skipped, StageStatus.Active), (orchestrator.SaveStage, orchestrator.MissionsStage, orchestrator.ExportStage));

        orchestrator.Apply(new FetchProgress { State = AppState.Success });
        Assert.Equal(StageStatus.Skipped, orchestrator.ExportStage);
    }

    [Fact]
    public void Apply_StageTransitions_WithExport_EndsDone() {
        using var orchestrator = MakeDetached(new ManualClock());

        orchestrator.Apply(new FetchProgress { State = AppState.FetchingSave });
        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, Total = 1 });
        Assert.Equal((StageStatus.Done, StageStatus.Active), (orchestrator.SaveStage, orchestrator.MissionsStage));

        orchestrator.Apply(new FetchProgress { State = AppState.ExportingData, Total = 1, Finished = 1 });
        orchestrator.Apply(new FetchProgress { State = AppState.Success, ExportedFiles = ["a.csv"] });

        Assert.Equal((StageStatus.Done, StageStatus.Done), (orchestrator.MissionsStage, orchestrator.ExportStage));
        Assert.Equal(["a.csv"], orchestrator.ExportedFiles);
    }

    [Fact]
    public void Apply_FailureDuringMissions_FailsActiveStage() {
        using var orchestrator = MakeDetached(new ManualClock());

        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, Total = 3 });
        orchestrator.Apply(new FetchProgress { State = AppState.Failed, Total = 3, Finished = 3, Failed = 1 });

        Assert.Equal((StageStatus.Done, StageStatus.Failed, StageStatus.Pending), (orchestrator.SaveStage, orchestrator.MissionsStage, orchestrator.ExportStage));
    }

    [Fact]
    public void Eta_NullUntilThreeFinished_ThenProjectsRemaining() {
        var clock = new ManualClock();
        using var orchestrator = MakeDetached(clock);

        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, Total = 10 });
        clock.Advance(TimeSpan.FromSeconds(4));
        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, Total = 10, Finished = 2 });
        Assert.Null(orchestrator.Eta);

        clock.Advance(TimeSpan.FromSeconds(2));
        orchestrator.Apply(new FetchProgress { State = AppState.FetchingMissions, Total = 10, Finished = 3 });
        Assert.Equal(TimeSpan.FromSeconds(14), orchestrator.Eta);
    }

    [Fact]
    public void Apply_ActiveSegment_SetsCurrentMissionLabel() {
        using var orchestrator = MakeDetached(new ManualClock());

        orchestrator.Apply(Segment("m1", "Fetch", SegmentStatus.Active));

        Assert.Equal("Henerprise, Epic, 2026-09-01", orchestrator.CurrentMissionLabel);
    }

    [Fact]
    public async Task StopFetch_CancelsInFlightFetch_YieldsInterrupted() {
        var db = new FakeIndexedDb();
        var ids = Enumerable.Range(0, 20).Select(i => $"m{i}").ToArray();
        FetchOrchestrator? orchestrator = null;
        var handler = new RoutingHandler(FirstContactBody(ids), CompleteMissionBody, onCompleteMissionRequest: _ => orchestrator!.StopFetch());
        orchestrator = Make(db, handler);

        await orchestrator.StartFetchAsync(Eid);

        Assert.Equal(AppState.Interrupted, orchestrator.TerminalState);
    }

    [Fact]
    public async Task Changed_FiresOnCompletion_WithFetchLog() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var orchestrator = Make(db, handler);

        bool firedAtSuccess = false;
        orchestrator.Changed += () => {
            if (orchestrator.TerminalState == AppState.Success) {
                firedAtSuccess = true;
            }
        };

        await orchestrator.StartFetchAsync(Eid);

        Assert.True(firedAtSuccess);
        Assert.Contains(orchestrator.Log, e => e.Text.StartsWith("successfully fetched backup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartFetchAsync_OnSuccess_RaisesFetchSucceededWithAccountId() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var orchestrator = Make(db, handler);

        var succeeded = new List<string>();
        orchestrator.FetchSucceeded += succeeded.Add;

        await orchestrator.StartFetchAsync(Eid);

        Assert.Equal(AppState.Success, orchestrator.TerminalState);
        Assert.Equal(Eid, Assert.Single(succeeded));
    }

    [Fact]
    public async Task AttachedLedgerDataHub_InvalidatesFetchedAccountOnSuccess() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var orchestrator = Make(db, handler);

        using var hub = new LedgerDataHub(
            _ => Task.FromResult<IReadOnlyList<DatabaseMission>?>([]),
            _ => Task.FromResult<Dictionary<string, List<MissionDrop>>?>(null),
            TimeSpan.FromMinutes(5),
            new ManualClock());
        var invalidated = new List<string>();
        hub.AccountInvalidated += invalidated.Add;
        hub.AttachFetch(orchestrator);

        await orchestrator.StartFetchAsync(Eid);

        Assert.Equal(Eid, Assert.Single(invalidated));

        hub.Dispose();
        await orchestrator.StartFetchAsync(Eid);

        Assert.Single(invalidated);
    }

    [Fact]
    public async Task StartFetchAsync_WhileInFlight_CancelsPreviousAndStartsFresh() {
        var db = new FakeIndexedDb();
        var ids = Enumerable.Range(0, 20).Select(i => $"m{i}").ToArray();
        FetchOrchestrator? orchestrator = null;
        Task? reentrantFetch = null;
        bool? initialRequestTokenCancelledAfterReentry = null;
        var handler = new RoutingHandler(FirstContactBody(ids), CompleteMissionBody,
            onCompleteMissionRequestWithToken: (_, requestToken) => {
                if (reentrantFetch is not null) {
                    return;
                }

                reentrantFetch = orchestrator!.StartFetchAsync(Eid);
                initialRequestTokenCancelledAfterReentry = requestToken.IsCancellationRequested;
            });
        orchestrator = Make(db, handler);

        var initialFetch = orchestrator.StartFetchAsync(Eid);
        await initialFetch;
        if (reentrantFetch is not null) {
            await reentrantFetch;
        }

        Assert.True(initialRequestTokenCancelledAfterReentry);
        Assert.Equal(AppState.Success, orchestrator.TerminalState);
    }
}
