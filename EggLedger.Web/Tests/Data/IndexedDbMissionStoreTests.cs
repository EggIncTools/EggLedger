using System.IO.Compression;
using EggLedger.Domain.Api;
using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Data;
using Ei;
using ProtoBuf;

namespace EggLedger.Web.Tests.Data;

public sealed class IndexedDbMissionStoreTests {
    private static (IndexedDbMissionStore Store, FakeIndexedDb Db) Make() {
        var db = new FakeIndexedDb();
        var decoder = new LocalApiPayloadDecoder(new ApiClient());
        return (new IndexedDbMissionStore(db, decoder), db);
    }

    private static MissionRow MissionMeta(string playerId, string missionId, double start, int ship = 0) => new() {
        PlayerId = playerId,
        MissionId = missionId,
        StartTimestamp = start,
        Ship = ship,
        ReturnTimestamp = start + 100,
    };

    private static byte[] PackPayload(CompleteMissionResponse resp) {
        using var inner = new MemoryStream();
        Serializer.Serialize(inner, resp);
        var auth = new AuthenticatedMessage { Message = inner.ToArray(), Compressed = false };

        using var authBytes = new MemoryStream();
        Serializer.Serialize(authBytes, auth);

        using var gzipped = new MemoryStream();
        using (var gz = new GZipStream(gzipped, CompressionMode.Compress, leaveOpen: true)) {
            var raw = authBytes.ToArray();
            gz.Write(raw, 0, raw.Length);
        }
        return gzipped.ToArray();
    }

    [Fact]
    public async Task GetCompleteMissionIdsAsync_ReturnsOnlyPlayerRows_OrderedByStart() {
        var (store, db) = Make();
        db.Seed("mission", MissionMeta("EI1", "m2", start: 200));
        db.Seed("mission", MissionMeta("EI1", "m1", start: 100));
        db.Seed("mission", MissionMeta("EI2", "other", start: 50));

        var ids = await store.GetCompleteMissionIdsAsync("EI1");

        Assert.Equal(new[] { "m1", "m2" }, ids);
    }

    [Fact]
    public async Task GetCompleteMissionIdsAsync_EmptyWhenNoRows() {
        var (store, _) = Make();
        var ids = await store.GetCompleteMissionIdsAsync("EI1");
        Assert.Empty(ids!);
    }

    [Fact]
    public async Task GetPlayerMissionStatsAsync_CountsAndMaxReturn() {
        var (store, db) = Make();
        db.Seed("mission", MissionMeta("EI1", "m1", start: 100));
        db.Seed("mission", MissionMeta("EI1", "m2", start: 500));
        db.Seed("mission", MissionMeta("EI2", "x", start: 999));

        var stats = await store.GetPlayerMissionStatsAsync("EI1");

        Assert.NotNull(stats);
        Assert.Equal(2, stats!.Value.Count);
        Assert.Equal(600, stats.Value.MaxReturnTimestamp);
    }

    [Fact]
    public async Task GetPlayerMissionStatsAsync_ZeroWhenNoRows() {
        var (store, _) = Make();
        var stats = await store.GetPlayerMissionStatsAsync("EI1");
        Assert.NotNull(stats);
        Assert.Equal(0, stats!.Value.Count);
        Assert.Equal(0, stats.Value.MaxReturnTimestamp);
    }

    [Fact]
    public async Task CountPendingFilterColsAsync_CountsShipMinusOne() {
        var (store, db) = Make();
        db.Seed("mission", MissionMeta("EI1", "done", start: 1, ship: 0));
        db.Seed("mission", MissionMeta("EI1", "pending1", start: 2, ship: -1));
        db.Seed("mission", MissionMeta("EI1", "pending2", start: 3, ship: -1));

        Assert.Equal(2, await store.CountPendingFilterColsAsync("EI1"));
    }

    [Fact]
    public async Task GetCompleteMissionAsync_DecodesGzippedAuthenticatedPayload() {
        var (store, db) = Make();
        var resp = new CompleteMissionResponse {
            Success = true,
            Info = new MissionInfo { Identifier = "m1", Ship = MissionInfo.Spaceship.Henerprise },
        };
        resp.Artifacts.Add(new CompleteMissionResponse.SecureArtifactSpec {
            Spec = new ArtifactSpec { name = ArtifactSpec.Name.TachyonDeflector },
        });

        db.Seed("mission", new MissionRow {
            PlayerId = "EI1",
            MissionId = "m1",
            StartTimestamp = 12345,
            Ship = 0,
            CompletePayload = PackPayload(resp),
        });

        var got = await store.GetCompleteMissionAsync("EI1", "m1");

        Assert.NotNull(got);
        Assert.True(got!.Success);
        Assert.Equal("m1", got.Info!.Identifier);
        Assert.Equal(MissionInfo.Spaceship.Henerprise, got.Info.Ship);
        Assert.Single(got.Artifacts);
        Assert.Equal(ArtifactSpec.Name.TachyonDeflector, got.Artifacts[0].Spec!.name);

        Assert.Equal(12345, got.Info.StartTimeDerived);
    }

    [Fact]
    public async Task GetCompleteMissionAsync_NullOnCacheMiss() {
        var (store, db) = Make();
        db.Seed("mission", MissionMeta("EI1", "m1", start: 1));
        Assert.Null(await store.GetCompleteMissionAsync("EI1", "nope"));
    }

    [Fact]
    public async Task StreamPlayerCompleteMissionsAsync_VisitsDecodedMissionsInOrder() {
        var (store, db) = Make();
        db.Seed("mission", PayloadRow("EI1", "m2", start: 200, identifier: "m2"));
        db.Seed("mission", PayloadRow("EI1", "m1", start: 100, identifier: "m1"));

        var seen = new List<string>();
        bool ok = await store.StreamPlayerCompleteMissionsAsync("EI1", cm => seen.Add(cm.Info!.Identifier));

        Assert.True(ok);
        Assert.Equal(new[] { "m1", "m2" }, seen);
    }

    private sealed class CountingDecoder : IApiPayloadDecoder {
        public int Calls;
        public Task<EggIncFirstContactResponse> DecodeFirstContactAsync(byte[] rawPayload, CancellationToken ct = default) =>
            Task.FromResult(new EggIncFirstContactResponse());
        public Task<CompleteMissionResponse> DecodeCompleteMissionAsync(byte[] rawPayload, CancellationToken ct = default) {
            Calls++;
            return Task.FromResult(new CompleteMissionResponse {
                Success = true,
                Info = new MissionInfo { Identifier = "m1" },
            });
        }
    }

    [Fact]
    public async Task GetCompleteMissionAsync_SecondCall_DoesNotReDecode() {
        var decoder = new CountingDecoder();
        var db = new FakeIndexedDb();
        db.Seed("mission", PayloadRow("EI1", "m1", start: 1, identifier: "m1"));
        var store = new IndexedDbMissionStore(db, decoder);

        var first = await store.GetCompleteMissionAsync("EI1", "m1");
        var second = await store.GetCompleteMissionAsync("EI1", "m1");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, decoder.Calls);
    }

    [Fact]
    public async Task GetCompleteMissionAsync_DistinctMissions_DecodeSeparately() {
        var decoder = new CountingDecoder();
        var db = new FakeIndexedDb();
        db.Seed("mission", PayloadRow("EI1", "m1", start: 1, identifier: "m1"));
        db.Seed("mission", PayloadRow("EI1", "m2", start: 2, identifier: "m2"));
        var store = new IndexedDbMissionStore(db, decoder);

        await store.GetCompleteMissionAsync("EI1", "m1");
        await store.GetCompleteMissionAsync("EI1", "m2");

        Assert.Equal(2, decoder.Calls);
    }

    [Fact]
    public async Task InsertCompleteMissionAsync_EvictsCachedDecode() {
        var decoder = new CountingDecoder();
        var db = new FakeIndexedDb();
        db.Seed("mission", PayloadRow("EI1", "m1", start: 1, identifier: "m1"));
        var store = new IndexedDbMissionStore(db, decoder);

        await store.GetCompleteMissionAsync("EI1", "m1");
        await store.InsertCompleteMissionAsync("EI1", "m1", 1, [1, 2, 3], 0,
            default, new CompleteMissionResponse());
        await store.GetCompleteMissionAsync("EI1", "m1");

        Assert.Equal(2, decoder.Calls);
    }

    private sealed class RecordingDb : IIndexedDb {
        private readonly FakeIndexedDb _inner = new();
        public List<string> ProjectedTypes { get; } = [];
        public List<string> FullTypes { get; } = [];
        public void Seed(string s, object r) => _inner.Seed(s, r);

        public ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) {
            ProjectedTypes.Add(typeof(T).Name);
            return _inner.GetAllByIndexProjectedAsync<T>(store, index, value);
        }
        public ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) {
            FullTypes.Add(typeof(T).Name);
            return _inner.GetAllByIndexAsync<T>(store, index, value);
        }
        public ValueTask<T[]> GetAllAsync<T>(string store) => _inner.GetAllAsync<T>(store);
        public ValueTask<T?> GetAsync<T>(string store, object key) => _inner.GetAsync<T>(store, key);
        public ValueTask PutAsync(string store, object value) => _inner.PutAsync(store, value);
        public ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) => _inner.PutManyAsync(store, values);
        public ValueTask DeleteAsync(string store, object key) => _inner.DeleteAsync(store, key);
        public ValueTask ClearAsync(string store) => _inner.ClearAsync(store);
        public ValueTask<int> CountAsync(string store) => _inner.CountAsync(store);
    }

    [Fact]
    public async Task GetCompleteMissionIdsAsync_UsesProjectedRead() {
        var db = new RecordingDb();
        db.Seed("mission", MissionMeta("EI1", "m1", start: 1));
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));

        await store.GetCompleteMissionIdsAsync("EI1");

        Assert.Contains("MissionMetaRow", db.ProjectedTypes);
        Assert.DoesNotContain("MissionRow", db.FullTypes);
    }

    [Fact]
    public async Task GetPlayerMissionMetaAsync_ProjectedRead_StillBuildsRows() {
        var db = new RecordingDb();
        db.Seed("mission", MissionMeta("EI1", "m1", start: 100, ship: 9));
        db.Seed("mission", MissionMeta("EI1", "m2", start: 200, ship: 3));
        var store = new IndexedDbMissionStore(db, new LocalApiPayloadDecoder(new ApiClient()));

        var rows = await store.GetPlayerMissionMetaAsync("EI1");

        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        Assert.Contains("MissionMetaRow", db.ProjectedTypes);
    }

    private static DatabaseMission InFlight(string missionId, long launch, int missionType = 0) => new() {
        MissiondId = missionId,
        LaunchDT = launch,
        ReturnDT = launch + 3600,
        Ship = MissionInfo.Spaceship.Henerprise,
        ShipString = "Henerprise",
        ShipEnumString = "HENERPRISE",
        DurationType = MissionInfo.DurationType.Epic,
        DurationString = "Extended",
        Level = 4,
        Capacity = 30,
        NominalCapcity = 28,
        IsDubCap = true,
        Target = "BOOK_OF_BASAN",
        TargetInt = 7,
        MissionType = missionType,
        MissionTypeString = "Standard",
    };

    [Fact]
    public async Task ReplaceInFlightMissionsAsync_RoundTripsEveryField() {
        var (store, _) = Make();

        Assert.True(await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("f1", 100)]));
        var got = await store.GetInFlightMissionsAsync("EI1");

        var one = Assert.Single(got);
        var expected = InFlight("f1", 100);
        Assert.Equal(expected.MissiondId, one.MissiondId);
        Assert.Equal(expected.LaunchDT, one.LaunchDT);
        Assert.Equal(expected.ReturnDT, one.ReturnDT);
        Assert.Equal(expected.Ship, one.Ship);
        Assert.Equal(expected.ShipEnumString, one.ShipEnumString);
        Assert.Equal(expected.DurationType, one.DurationType);
        Assert.Equal(expected.Level, one.Level);
        Assert.Equal(expected.Capacity, one.Capacity);
        Assert.Equal(expected.NominalCapcity, one.NominalCapcity);
        Assert.True(one.IsDubCap);
        Assert.Equal(expected.Target, one.Target);
        Assert.Equal(expected.TargetInt, one.TargetInt);
        Assert.Equal(expected.MissionTypeString, one.MissionTypeString);
    }

    [Fact]
    public async Task ReplaceInFlightMissionsAsync_DropsMissionsMissingFromTheNewSet() {
        var (store, _) = Make();
        await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("f1", 100), InFlight("f2", 200)]);

        await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("f2", 250)]);
        var got = await store.GetInFlightMissionsAsync("EI1");

        var one = Assert.Single(got);
        Assert.Equal("f2", one.MissiondId);
        Assert.Equal(250, one.LaunchDT);
    }

    [Fact]
    public async Task ReplaceInFlightMissionsAsync_EmptySetClearsThePlayer() {
        var (store, _) = Make();
        await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("f1", 100)]);

        await store.ReplaceInFlightMissionsAsync("EI1", []);

        Assert.Empty(await store.GetInFlightMissionsAsync("EI1"));
    }

    [Fact]
    public async Task ReplaceInFlightMissionsAsync_LeavesOtherPlayersAlone() {
        var (store, _) = Make();
        await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("f1", 100)]);
        await store.ReplaceInFlightMissionsAsync("EI2", [InFlight("f9", 900)]);

        await store.ReplaceInFlightMissionsAsync("EI1", []);

        Assert.Empty(await store.GetInFlightMissionsAsync("EI1"));
        Assert.Equal("f9", Assert.Single(await store.GetInFlightMissionsAsync("EI2")).MissiondId);
    }

    [Fact]
    public async Task GetInFlightMissionsAsync_OrdersByLaunchTime() {
        var (store, _) = Make();
        await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("late", 900), InFlight("early", 100)]);

        var got = await store.GetInFlightMissionsAsync("EI1");

        Assert.Equal(new[] { "early", "late" }, got.Select(m => m.MissiondId).ToArray());
    }

    [Fact]
    public async Task GetInFlightMissionsAsync_SkipsUndeserializableRows() {
        var (store, db) = Make();
        await store.ReplaceInFlightMissionsAsync("EI1", [InFlight("f1", 100)]);
        db.Seed("inflight_mission", new InFlightMissionRow {
            PlayerId = "EI1",
            MissionId = "bad",
            CapturedAt = 1,
            Payload = "{not json",
        });

        var got = await store.GetInFlightMissionsAsync("EI1");

        Assert.Equal("f1", Assert.Single(got).MissiondId);
    }

    [Fact]
    public async Task GetInFlightMissionsAsync_EmptyForUnknownPlayer() {
        var (store, _) = Make();
        Assert.Empty(await store.GetInFlightMissionsAsync("EI1"));
    }

    private static MissionRow PayloadRow(string playerId, string missionId, double start, string identifier) {
        var resp = new CompleteMissionResponse {
            Success = true,
            Info = new MissionInfo { Identifier = identifier },
        };
        return new MissionRow {
            PlayerId = playerId,
            MissionId = missionId,
            StartTimestamp = start,
            Ship = 0,
            CompletePayload = PackPayload(resp),
        };
    }
}
