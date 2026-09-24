using EggLedger.Domain.Ei;
using Ei;

namespace EggLedger.Domain.Tests;

public class BackupExtensionsTests {
    [Theory]
    [InlineData(new[] { 1, 2, 3, 4 }, 10.0)]
    [InlineData(new int[0], 0.0)]
    public void Sum(int[] values, double want) =>
        Assert.Equal(want, BackupExtensions.Sum(values, v => (double)v));

    [Fact]
    public void Validate_ErrorCode() {
        var fc = new EggIncFirstContactResponse { ErrorCode = 1 };
        Assert.NotNull(fc.Validate());
    }

    [Fact]
    public void Validate_NilBackup() {
        var fc = new EggIncFirstContactResponse { ErrorCode = 0 };
        Assert.NotNull(fc.Validate());
    }

    [Fact]
    public void Validate_Valid() {
        var game = new Backup.Game();
        game.EpicResearchs.Add(new Backup.ResearchItem { Id = "soul_eggs", Level = 140 });
        var fc = new EggIncFirstContactResponse {
            ErrorCode = 0,
            Backup = new Backup {
                game = game,
                settings = new Backup.Settings(),
                ArtifactsDb = new ArtifactsDB(),
            },
        };
        Assert.Null(fc.Validate());
    }

    [Fact]
    public void GetEarningsBonus_BaseCase() {
        var b = new Backup {
            game = new Backup.Game { SoulEggsD = 0, EggsOfProphecy = 0 },
            virtue = new Backup.Virtue(),
        };
        Assert.Equal(0.0, b.GetEarningsBonus());
    }

    [Fact]
    public void GetEarningsBonus_SoulEggsOnly() {
        var b = new Backup {
            game = new Backup.Game { SoulEggsD = 1000, EggsOfProphecy = 0 },
            virtue = new Backup.Virtue(),
        };
        Assert.Equal(10000.0, b.GetEarningsBonus());
    }

    [Fact]
    public void GetEarningsBonus_WithEpicResearch() {
        var game = new Backup.Game { SoulEggsD = 1000, EggsOfProphecy = 0 };
        game.EpicResearchs.Add(new Backup.ResearchItem { Id = "soul_eggs", Level = 140 });
        var b = new Backup { game = game, virtue = new Backup.Virtue() };
        Assert.Equal(150000.0, b.GetEarningsBonus());
    }

    [Fact]
    public void GetEarningsBonus_ProphecyEggs() {
        var game = new Backup.Game { SoulEggsD = 1000, EggsOfProphecy = 10 };
        game.EpicResearchs.Add(new Backup.ResearchItem { Id = "soul_eggs", Level = 140 });
        var b = new Backup { game = game, virtue = new Backup.Virtue() };
        var got = b.GetEarningsBonus();
        Assert.InRange(got, 244000, 245000);
    }

    [Fact]
    public void GetEarningsBonus_WithTE() {
        var game = new Backup.Game { SoulEggsD = 1000, EggsOfProphecy = 0 };
        game.EpicResearchs.Add(new Backup.ResearchItem { Id = "soul_eggs", Level = 140 });
        var b = new Backup {
            game = game,
            virtue = new Backup.Virtue { EovEarneds = [100] },
        };
        var got = b.GetEarningsBonus();
        Assert.InRange(got, 405000, 406000);
    }
}
