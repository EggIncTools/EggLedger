using EggLedger.Domain.Ei;
using Ei;

namespace EggLedger.Domain.Tests;

public class MissionExtensionsTests {
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(30, "30s")]
    [InlineData(90, "1m")]
    [InlineData((3 * 3600) + (45 * 60), "3h45m")]
    [InlineData((2 * 86400) + (3 * 3600) + (15 * 60), "2d3h15m")]
    public void GetDurationString(int secs, string want) {
        var m = new MissionInfo { DurationSeconds = secs };
        Assert.Equal(want, m.GetDurationString());
    }

    [Theory]
    [InlineData(MissionInfo.DurationType.Tutorial, "Tutorial")]
    [InlineData(MissionInfo.DurationType.Short, "Short")]
    [InlineData(MissionInfo.DurationType.Long, "Standard")]
    [InlineData(MissionInfo.DurationType.Epic, "Extended")]
    public void DurationTypeDisplay(MissionInfo.DurationType d, string want) =>
        Assert.Equal(want, d.Display());

    [Theory]
    [InlineData(MissionInfo.MissionType.Standard, "Home")]
    [InlineData(MissionInfo.MissionType.Virtue, "Virtue")]
    public void MissionTypeDisplay(MissionInfo.MissionType m, string want) =>
        Assert.Equal(want, m.Display());

    [Fact]
    public void GetCompletedMissions_Deduplication() {
        const string id = "mission-abc";
        var m1 = new MissionInfo { Identifier = id, status = MissionInfo.Status.Complete };
        var m2 = new MissionInfo { Identifier = id, status = MissionInfo.Status.Complete };

        var fc = new EggIncFirstContactResponse { Backup = new Backup { ArtifactsDb = new ArtifactsDB() } };
        fc.Backup.ArtifactsDb.MissionArchives.Add(m1);
        fc.Backup.ArtifactsDb.MissionArchives.Add(m2);

        var missions = fc.GetCompletedMissions();
        Assert.Single(missions);
    }

    [Fact]
    public void GetCompletedMissions_FiltersNonCompleted() {
        const string id1 = "m-complete";
        const string id2 = "m-exploring";

        var fc = new EggIncFirstContactResponse { Backup = new Backup { ArtifactsDb = new ArtifactsDB() } };
        fc.Backup.ArtifactsDb.MissionArchives.Add(
            new MissionInfo { Identifier = id1, status = MissionInfo.Status.Complete });
        fc.Backup.ArtifactsDb.MissionArchives.Add(
            new MissionInfo { Identifier = id2, status = MissionInfo.Status.Exploring });

        var missions = fc.GetCompletedMissions();
        Assert.Single(missions);
        Assert.Equal(id1, missions[0].Identifier);
    }

    [Fact]
    public void GetCompletedMissions_SortedByStartTime() {
        const string id1 = "m-later";
        const string id2 = "m-earlier";

        var fc = new EggIncFirstContactResponse { Backup = new Backup { ArtifactsDb = new ArtifactsDB() } };
        fc.Backup.ArtifactsDb.MissionArchives.Add(
            new MissionInfo { Identifier = id1, status = MissionInfo.Status.Complete, StartTimeDerived = 2000 });
        fc.Backup.ArtifactsDb.MissionArchives.Add(
            new MissionInfo { Identifier = id2, status = MissionInfo.Status.Complete, StartTimeDerived = 1000 });

        var missions = fc.GetCompletedMissions();
        Assert.Equal(2, missions.Count);
        Assert.Equal(id2, missions[0].Identifier);
    }

    [Fact]
    public void SpaceshipName_KnownShip() =>
        Assert.Equal("Chicken One", MissionInfo.Spaceship.ChickenOne.Name());

    [Fact]
    public void SpaceshipName_UnknownShip() {
        var unknown = (MissionInfo.Spaceship)9999;
        Assert.False(string.IsNullOrEmpty(unknown.Name()));
    }
}
