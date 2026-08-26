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
        var fetch = new FetchService(api, store, settings, accounts, new LocalApiPayloadDecoder(api));
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





    private sealed class RoutingHandler : HttpMessageHandler {
        private readonly string _firstContactBody;
        private readonly Func<string, string?> _completeMission;
        private readonly Action<string>? _onCompleteMissionRequest;
        private readonly Action<string, CancellationToken>? _onCompleteMissionRequestWithToken;

        public RoutingHandler(string firstContactBody, Func<string, string?> completeMission, Action<string>? onCompleteMissionRequest = null, Action<string, CancellationToken>? onCompleteMissionRequestWithToken = null) {
            _firstContactBody = firstContactBody;
            _completeMission = completeMission;
            _onCompleteMissionRequest = onCompleteMissionRequest;
            _onCompleteMissionRequestWithToken = onCompleteMissionRequestWithToken;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            string path = request.RequestUri!.AbsolutePath;
            string form = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (path.EndsWith(ApiClient.FirstContactEndpoint, StringComparison.Ordinal)) {
                return Ok(_firstContactBody);
            }
            if (path.EndsWith(ApiClient.CompleteMissionEndpoint, StringComparison.Ordinal)) {
                string id = ExtractMissionId(form);
                _onCompleteMissionRequest?.Invoke(id);
                _onCompleteMissionRequestWithToken?.Invoke(id, cancellationToken);
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

    [Fact]
    public async Task StartFetchAsync_SegmentOnlyReport_CarriesForwardCounts() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var orchestrator = Make(db, handler);

        var reports = new List<FetchProgress>();
        orchestrator.Changed += () => {
            if (orchestrator.Progress is { } p) {
                reports.Add(p);
            }
        };

        await orchestrator.StartFetchAsync(Eid);



        var firstCounts = reports.First(r => r.State == AppState.FetchingMissions && r.Segment is null);
        var segmentReports = reports.Where(r => r.Segment is not null).ToList();
        Assert.NotEmpty(segmentReports);
        foreach (var seg in segmentReports) {
            Assert.Equal(firstCounts.Total, seg.Total);
            Assert.Equal(firstCounts.Failed, seg.Failed);
            Assert.Equal(firstCounts.Retried, seg.Retried);
        }
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
    public async Task Changed_FiresOnProgressAndOnCompletion() {
        var db = new FakeIndexedDb();
        var handler = new RoutingHandler(FirstContactBody(["m1"]), CompleteMissionBody);
        var orchestrator = Make(db, handler);

        bool firedDuringFetchingMissions = false;
        bool firedAtSuccess = false;
        orchestrator.Changed += () => {
            if (orchestrator.TerminalState == AppState.Success) {
                firedAtSuccess = true;
            } else if (orchestrator.Progress?.State == AppState.FetchingMissions) {
                firedDuringFetchingMissions = true;
            }
        };

        await orchestrator.StartFetchAsync(Eid);



        Assert.True(firedDuringFetchingMissions);
        Assert.True(firedAtSuccess);
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
            TimeSpan.FromMinutes(5));
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
