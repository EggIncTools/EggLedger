using EggLedger.Domain.Ei;
using EggLedger.Domain.MissionQuery;
using Ei;

namespace EggLedger.Domain.Tests.MissionQuery;

public class MissionQueryHandlersTests {
    private static (MissionQueryHandlers h, FakeMissionStore store, FakeQuality q) NewSut() {
        var store = new FakeMissionStore();
        var quality = new FakeQuality();
        return (new MissionQueryHandlers(store, quality, new FakeMissionCompiler()), store, quality);
    }

    private static CompleteMissionResponse Mission(string id, params ArtifactSpec[] specs) {
        var cm = new CompleteMissionResponse { Info = new MissionInfo { Identifier = id } };
        foreach (var s in specs) {
            cm.Artifacts.Add(new CompleteMissionResponse.SecureArtifactSpec { Spec = s });
        }
        return cm;
    }

    [Fact]
    public async Task GetMissionIds_ReturnsStoreValue() {
        var (h, store, _) = NewSut();
        store.CompleteMissionIds = new[] { "a", "b" };
        Assert.Equal(new[] { "a", "b" }, await h.GetMissionIdsAsync("p"));
    }

    [Fact]
    public async Task GetExistingData_DropsZeroCountAndErrors() {
        var (h, store, _) = NewSut();
        store.KnownAccounts.Add(new KnownAccount { Id = "a", Nickname = "A", EBString = "eb", AccountColor = "#fff" });
        store.KnownAccounts.Add(new KnownAccount { Id = "b", Nickname = "B" });
        store.KnownAccounts.Add(new KnownAccount { Id = "c", Nickname = "C" });
        store.Stats["a"] = new PlayerMissionStats(5, 123.0);
        store.Stats["b"] = new PlayerMissionStats(0, 0);
        store.Stats["c"] = null;

        var got = await h.GetExistingDataAsync();

        Assert.Single(got);
        Assert.Equal("a", got[0].Id);
        Assert.Equal("A", got[0].Nickname);
        Assert.Equal(5, got[0].MissionCount);
        Assert.Equal("eb", got[0].EBString);
        Assert.Equal("#fff", got[0].AccountColor);
        Assert.Equal(123.0, got[0].LastMissionReturnDT);
    }

    [Fact]
    public async Task GetExistingData_PreservesOrder() {
        var (h, store, _) = NewSut();
        store.KnownAccounts.Add(new KnownAccount { Id = "x" });
        store.KnownAccounts.Add(new KnownAccount { Id = "y" });
        store.Stats["x"] = new PlayerMissionStats(1, 0);
        store.Stats["y"] = new PlayerMissionStats(2, 0);

        var got = await h.GetExistingDataAsync();

        Assert.Equal(new[] { "x", "y" }, got.Select(a => a.Id));
    }

    [Fact]
    public async Task ViewMissionsOfEid_FastPath_WhenNoPending() {
        var (h, store, _) = NewSut();
        var rows = new IMissionRow[] { new FakeMissionRow("m1") };
        store.PendingFilterCols["eid"] = 0;
        store.MissionMeta["eid"] = rows;

        store.PlayerCompleteMissions["eid"] = new[] { Mission("ignored") };

        var got = await h.ViewMissionsOfEidAsync("eid");

        Assert.Same(rows, got);
        Assert.Empty(store.BackfillsQueued);
    }

    [Fact]
    public async Task ViewMissionsOfEid_SlowPath_CompilesAndQueuesBackfill() {
        var (h, store, _) = NewSut();
        store.PendingFilterCols["eid"] = 2;
        store.PlayerCompleteMissions["eid"] = new[] { Mission("m1"), Mission("m2") };

        var got = await h.ViewMissionsOfEidAsync("eid");

        Assert.NotNull(got);
        Assert.Equal(2, got!.Count);
        Assert.Equal(new[] { "m1", "m2" }, got.Cast<FakeMissionRow>().Select(r => r.Id));
        Assert.Equal(new[] { "eid" }, store.BackfillsQueued);
    }

    [Fact]
    public async Task ViewMissionsOfEid_SlowPath_NullCountSuppressesBackfill() {
        var (h, store, _) = NewSut();

        store.PendingFilterCols["eid"] = null;
        store.PlayerCompleteMissions["eid"] = new[] { Mission("m1") };

        var got = await h.ViewMissionsOfEidAsync("eid");

        Assert.NotNull(got);
        Assert.Single(got!);
        Assert.Empty(store.BackfillsQueued);
    }

    [Fact]
    public async Task ViewMissionsOfEid_SlowPath_NullOnStoreError() {
        var (h, store, _) = NewSut();
        store.PendingFilterCols["eid"] = 1;
        store.PlayerCompleteMissions["eid"] = null;

        Assert.Null(await h.ViewMissionsOfEidAsync("eid"));
        Assert.Empty(store.BackfillsQueued);
    }

    [Fact]
    public void GetDurationConfigs_ShapesShipsAndDurations() {
        var mp = new ArtifactsConfigurationResponse.MissionParameters {
            Ship = MissionInfo.Spaceship.Atreggies,
            LevelMissionRequirements = [0, 10, 20, 30],
        };
        mp.Durations.Add(new ArtifactsConfigurationResponse.MissionParameters.Duration {
            DurationType = MissionInfo.DurationType.Long,
            MinQuality = 1.5f,
            MaxQuality = 4.0f,
            LevelQualityBump = 0.25f,
        });

        var got = MissionQueryHandlers.GetDurationConfigs(new[] { mp });

        Assert.Single(got);
        Assert.Equal(MissionInfo.Spaceship.Atreggies, got[0].Ship);
        Assert.Single(got[0].Durations);
        var d = got[0].Durations[0];
        Assert.Equal(MissionInfo.DurationType.Long, d.DurationType);
        Assert.Equal(1.5, d.MinQuality, 5);
        Assert.Equal(4.0, d.MaxQuality, 5);
        Assert.Equal(0.25, d.LevelQualityBump, 5);
        Assert.Equal(4, d.MaxLevels);
    }

    [Fact]
    public void GetDurationConfigs_NullLevelReqsGivesZeroMaxLevels() {
        var mp = new ArtifactsConfigurationResponse.MissionParameters {
            Ship = MissionInfo.Spaceship.ChickenOne,
        };
        mp.Durations.Add(new ArtifactsConfigurationResponse.MissionParameters.Duration {
            DurationType = MissionInfo.DurationType.Short,
        });

        var got = MissionQueryHandlers.GetDurationConfigs(new[] { mp });

        Assert.Equal(0, got[0].Durations[0].MaxLevels);
    }

    [Fact]
    public async Task GetShipDrops_NullOnCacheMiss() {
        var (h, _, _) = NewSut();
        Assert.Null(await h.GetShipDropsAsync("p", "m"));
    }

    [Fact]
    public async Task GetShipDrops_ClassifiesAllSpecTypes() {
        var (h, store, q) = NewSut();
        var artifact = new ArtifactSpec {
            name = ArtifactSpec.Name.TachyonDeflector,
            level = ArtifactSpec.Level.Greater,
            rarity = ArtifactSpec.Rarity.Legendary,
        };
        var stone = new ArtifactSpec { name = ArtifactSpec.Name.TachyonStone, level = ArtifactSpec.Level.Normal };
        var fragment = new ArtifactSpec { name = ArtifactSpec.Name.TachyonStoneFragment };
        var ingredient = new ArtifactSpec { name = ArtifactSpec.Name.GoldMeteorite, level = ArtifactSpec.Level.Normal };
        q.Map[(ArtifactSpec.Name.TachyonDeflector, ArtifactSpec.Level.Greater, ArtifactSpec.Rarity.Legendary)] = 7.5;

        store.CompleteMissions[("p", "m")] = Mission("m", artifact, stone, fragment, ingredient);

        var drops = await h.GetShipDropsAsync("p", "m");

        Assert.NotNull(drops);
        Assert.Equal(4, drops!.Count);

        Assert.Equal("Artifact", drops[0].SpecType);
        Assert.Equal((int)ArtifactSpec.Name.TachyonDeflector, drops[0].Id);
        Assert.Equal("TACHYON_DEFLECTOR", drops[0].Name);
        Assert.Equal((int)ArtifactSpec.Level.Greater, drops[0].Level);
        Assert.Equal((int)ArtifactSpec.Rarity.Legendary, drops[0].Rarity);
        Assert.Equal(7.5, drops[0].Quality);
        Assert.Equal(artifact.name.InventoryVisualizerOrder(), drops[0].IVOrder);
        Assert.Equal(artifact.CombinedEffectString(), drops[0].EffectString);
        Assert.NotEqual("", drops[0].EffectString);


        Assert.Equal("Stone", drops[1].SpecType);
        Assert.Equal("TACHYON_STONE", drops[1].Name);
        Assert.Equal(stone.CombinedEffectString(), drops[1].EffectString);

        Assert.Equal("StoneFragment", drops[2].SpecType);
        Assert.Equal("TACHYON_STONE_FRAGMENT", drops[2].Name);
        Assert.Equal("", drops[2].EffectString);

        Assert.Equal("Ingredient", drops[3].SpecType);
        Assert.Equal("GOLD_METEORITE", drops[3].Name);
        Assert.Equal("", drops[3].EffectString);
    }

    [Fact]
    public async Task GetShipDrops_GameNameMatchesCasedName() {
        var (h, store, _) = NewSut();
        var spec = new ArtifactSpec {
            name = ArtifactSpec.Name.TachyonDeflector,
            level = ArtifactSpec.Level.Greater,
            rarity = ArtifactSpec.Rarity.Legendary,
        };
        store.CompleteMissions[("p", "m")] = Mission("m", spec);

        var drops = await h.GetShipDropsAsync("p", "m");

        Assert.Equal(spec.CasedName(), drops![0].GameName);
    }

    [Fact]
    public async Task GetAllPlayerDrops_MapsMissionIdsToDrops() {
        var (h, store, _) = NewSut();

        int frag = (int)ArtifactSpec.Name.TachyonStoneFragment;
        int gold = (int)ArtifactSpec.Name.GoldMeteorite;
        store.CompleteMissionIds = ["m1", "m2"];
        store.StoredDrops["p"] =
        [
            new StoredDrop("m1", frag, 0, 0),
            new StoredDrop("m2", gold, 0, 0),
            new StoredDrop("m2", frag, 0, 0),
        ];

        var got = await h.GetAllPlayerDropsAsync("p");

        Assert.NotNull(got);
        Assert.Equal(2, got!.Count);
        Assert.Single(got["m1"]);
        Assert.Equal("StoneFragment", got["m1"][0].SpecType);
        Assert.Equal(2, got["m2"].Count);
        Assert.Equal("Ingredient", got["m2"][0].SpecType);
        Assert.Equal("StoneFragment", got["m2"][1].SpecType);
    }

    [Fact]
    public async Task GetAllPlayerDrops_NoStoredDropsGivesEmptyMap() {
        var (h, store, _) = NewSut();
        store.CompleteMissionIds = [];
        store.StoredDrops["p"] = [];

        var got = await h.GetAllPlayerDropsAsync("p");

        Assert.NotNull(got);
        Assert.Empty(got!);
    }

    [Fact]
    public async Task GetAllPlayerDrops_ZeroDropMissionsAreSeededWithEmptyList() {
        var (h, store, _) = NewSut();


        int frag = (int)ArtifactSpec.Name.TachyonStoneFragment;
        store.CompleteMissionIds = ["m1", "m2"];
        store.StoredDrops["p"] = [new StoredDrop("m1", frag, 0, 0)];

        var got = await h.GetAllPlayerDropsAsync("p");

        Assert.NotNull(got);
        Assert.Equal(2, got!.Count);
        Assert.Single(got["m1"]);
        Assert.Empty(got["m2"]);
    }

    [Fact]
    public async Task GetAllPlayerDrops_SentinelRowExcludedFromDropsButMissionCounted() {
        var (h, store, _) = NewSut();


        int frag = (int)ArtifactSpec.Name.TachyonStoneFragment;
        store.CompleteMissionIds = ["m1", "m2"];
        store.StoredDrops["p"] =
        [
            new StoredDrop("m1", 0, 0, 0, DropIndex: -1),
            new StoredDrop("m2", frag, 0, 0),
        ];

        var got = await h.GetAllPlayerDropsAsync("p");

        Assert.NotNull(got);
        Assert.Equal(2, got!.Count);
        Assert.Empty(got["m1"]);
        Assert.Single(got["m2"]);
    }

    [Fact]
    public async Task GetAllPlayerDrops_NullOnStoreError() {
        var (h, _, _) = NewSut();

        Assert.Null(await h.GetAllPlayerDropsAsync("p"));
    }

    [Fact]
    public async Task GetAllPlayerDrops_NullMissionIds_NullOnStoreError() {
        var (h, store, _) = NewSut();
        store.StoredDrops["p"] = [];
        store.CompleteMissionIds = null;

        Assert.Null(await h.GetAllPlayerDropsAsync("p"));
    }
}
