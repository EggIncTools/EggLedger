using EggLedger.Domain.MissionQuery;
using Ei;

namespace EggLedger.Domain.Tests.MissionQuery;

public sealed class AccountFactoryTests {
    private const string Eid = "EI1234567890123456";

    [Fact]
    public void CarriesIdAndNickname() {
        var backup = new Backup {
            UserName = "Alice",
            game = new Backup.Game { SoulEggsD = 0, EggsOfProphecy = 0 },
        };

        var acct = AccountFactory.FromBackup(Eid, backup);

        Assert.Equal(Eid, acct.Id);
        Assert.Equal("Alice", acct.Nickname);
    }

    [Fact]
    public void AbbreviatesSoulEggsAndCountsProphecy() {
        var backup = new Backup {
            UserName = "Bob",
            game = new Backup.Game { SoulEggsD = 1_500_000_000, EggsOfProphecy = 42 },
        };

        var acct = AccountFactory.FromBackup(Eid, backup);


        Assert.Equal("1.50B", acct.SeString);
        Assert.Equal(42, acct.PeCount);
    }

    [Fact]
    public void SumsTruthEggsAcrossVirtueSlice() {
        var backup = new Backup {
            UserName = "Carol",
            game = new Backup.Game(),
            virtue = new Backup.Virtue { EovEarneds = [3, 5, 7] },
        };

        var acct = AccountFactory.FromBackup(Eid, backup);

        Assert.Equal(15, acct.TeCount);
    }

    [Fact]
    public void NoVirtueMeansZeroTruthEggs() {
        var backup = new Backup {
            UserName = "Dave",
            game = new Backup.Game(),
        };

        var acct = AccountFactory.FromBackup(Eid, backup);

        Assert.Equal(0, acct.TeCount);
    }

    [Fact]
    public void ToKnownAccountProjectsHeaderSubset() {
        var info = new AccountInfo {
            Id = Eid,
            Nickname = "Frank",
            EBString = "1.23q",
            AccountColor = "ff0000",
            SeString = "5.00B",
            PeCount = 10,
            TeCount = 2,
        };

        var known = info.ToKnownAccount();

        Assert.Equal(Eid, known.Id);
        Assert.Equal("Frank", known.Nickname);
        Assert.Equal("1.23q", known.EBString);
        Assert.Equal("ff0000", known.AccountColor);
    }
}
