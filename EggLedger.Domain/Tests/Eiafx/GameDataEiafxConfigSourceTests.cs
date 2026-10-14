using EggLedger.Domain.Eiafx;
using EggSharp.GameData;
using Ei;

namespace EggLedger.Domain.Tests.Eiafx;

public class GameDataEiafxConfigSourceTests {
    private static readonly ArtifactsConfigurationResponse Config = GameDataEiafxConfigSource.Instance.Config;
    private static readonly IReadOnlyDictionary<string, string> Docs = GameDataProvider.EmbeddedDocuments();

    private static ArtifactsConfigurationResponse.MissionParameters.Duration Duration(
        MissionInfo.Spaceship ship, MissionInfo.DurationType type) =>
        Config.mission_parameters.Single(m => m.Ship == ship).Durations.Single(d => d.DurationType == type);

    [Fact]
    public void EveryShipAndDuration_Maps() {
        var ships = MissionShipCatalog.Parse(Docs[MissionShipCatalog.DocumentId]);
        Assert.Equal(ships.Ships.Count, Config.mission_parameters.Count);
        Assert.Equal(
            ships.Ships.Sum(s => s.Durations.Count),
            Config.mission_parameters.Sum(m => m.Durations.Count));
    }

    [Fact]
    public void EveryArtifact_Maps() {
        var artifacts = ArtifactCatalog.Parse(Docs[ArtifactCatalog.DocumentId]);
        Assert.Equal(artifacts.Artifacts.Count, Config.artifact_parameters.Count);
        Assert.Equal(artifacts.CraftingLevels.Count, Config.CraftingLevelInfos.Count);
    }

    [Fact]
    public void ChickenOneLong_MatchesCatalog() {
        var d = Duration(MissionInfo.Spaceship.ChickenOne, MissionInfo.DurationType.Long);
        Assert.Equal(3600d, d.Seconds);
        Assert.Equal(5u, d.Capacity);
        Assert.Equal(1u, d.LevelCapacityBump);
        Assert.Equal(1.1f, d.Quality);
        Assert.Equal(1.7f, d.MaxQuality);
        Assert.Equal(0.14f, d.LevelQualityBump);
    }

    [Fact]
    public void AtreggiesEpic_MatchesCatalog() {
        var d = Duration(MissionInfo.Spaceship.Atreggies, MissionInfo.DurationType.Epic);
        Assert.Equal(345600d, d.Seconds);
        Assert.Equal(86u, d.Capacity);
        Assert.Equal(10u, d.LevelCapacityBump);
    }

    [Fact]
    public void ChickenNine_KeepsLevelRequirements() {
        var m = Config.mission_parameters.Single(p => p.Ship == MissionInfo.Spaceship.ChickenNine);
        Assert.Equal([4u, 10u], m.LevelMissionRequirements);
    }

    [Fact]
    public void ArtifactSpecs_RoundTripAfxIds() {
        var artifacts = ArtifactCatalog.Parse(Docs[ArtifactCatalog.DocumentId]);
        foreach (var (a, p) in artifacts.Artifacts.Zip(Config.artifact_parameters)) {
            Assert.Equal(a.AfxId, (int)p.Spec.name);
            Assert.Equal(a.AfxLevel, (int)p.Spec.level);
            Assert.Equal(a.AfxRarity, (int)p.Spec.rarity);
        }
    }
}
