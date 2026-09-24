using EggLedger.Domain.MissionQuery;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.State;

public sealed class LedgerFormattingTests {
    private static DatabaseAccount Acct(string id, string nick, int count) =>
        new() { Id = id, Nickname = nick, MissionCount = count };

    [Fact]
    public void SortByMissionCountDescendingOrdersByCount() {
        var input = new[] {
            Acct("EI1", "Low", 5),
            Acct("EI2", "High", 50),
            Acct("EI3", "Mid", 20),
        };

        var sorted = LedgerFormatting.SortByMissionCountDescending(input);

        Assert.Equal(new[] { "EI2", "EI3", "EI1" }, sorted.Select(a => a.Id));
    }

    [Fact]
    public void SortIsStableForEqualCounts() {
        var input = new[] {
            Acct("EI1", "First", 10),
            Acct("EI2", "Second", 10),
            Acct("EI3", "Third", 10),
        };

        var sorted = LedgerFormatting.SortByMissionCountDescending(input);

        Assert.Equal(new[] { "EI1", "EI2", "EI3" }, sorted.Select(a => a.Id));
    }

    [Theory]
    [InlineData(0, 1000, "")]
    [InlineData(2000, 1000, "")]
    [InlineData(1000, 1000, "")]
    public void FormatTimeSinceEmptyCases(double ret, double now, string expected) =>
        Assert.Equal(expected, LedgerFormatting.FormatTimeSince(ret, now));

    [Fact]
    public void FormatTimeSinceMinutesOnly() =>
        Assert.Equal("5m ago", LedgerFormatting.FormatTimeSince(0 + 1, 1 + 5 * 60));

    [Fact]
    public void FormatTimeSinceHoursAndMinutes() {
        double now = 100_000;
        double ret = now - (2 * 3600 + 15 * 60);
        Assert.Equal("2h15m ago", LedgerFormatting.FormatTimeSince(ret, now));
    }

    [Fact]
    public void FormatTimeSinceHoursOnly() {
        double now = 100_000;
        double ret = now - 3 * 3600;
        Assert.Equal("3h ago", LedgerFormatting.FormatTimeSince(ret, now));
    }

    [Fact]
    public void FormatTimeSinceDaysAndHours() {
        double now = 1_000_000;
        double ret = now - (2 * 86400 + 4 * 3600);
        Assert.Equal("2d4h ago", LedgerFormatting.FormatTimeSince(ret, now));
    }

    [Fact]
    public void FormatTimeSinceDaysOnly() {
        double now = 1_000_000;
        double ret = now - 3 * 86400;
        Assert.Equal("3d ago", LedgerFormatting.FormatTimeSince(ret, now));
    }

    [Fact]
    public void FilterAccountsBlankReturnsInput() {
        var input = new List<DatabaseAccount> { Acct("EI1", "Alice", 1) };
        Assert.Same(input, LedgerFormatting.FilterAccounts(input, ""));
    }

    [Fact]
    public void FilterAccountsMatchesIdOrNicknameCaseInsensitive() {
        var input = new List<DatabaseAccount> {
            Acct("EI1111111111111111", "Alice", 1),
            Acct("EI2222222222222222", "Bob", 1),
        };

        Assert.Single(LedgerFormatting.FilterAccounts(input, "alic"));
        Assert.Single(LedgerFormatting.FilterAccounts(input, "2222"));
        Assert.Empty(LedgerFormatting.FilterAccounts(input, "zzz"));
    }

    [Theory]
    [InlineData("  ei1234567890123456 ", "EI1234567890123456")]
    [InlineData("ei1", "EI1")]
    [InlineData("", "")]
    public void NormalizeEidTrimsAndUppercases(string raw, string expected) =>
        Assert.Equal(expected, LedgerFormatting.NormalizeEid(raw));

    [Theory]
    [InlineData("", "")]
    [InlineData("XY1234567890123456", "Player ID must start with \"EI\"")]
    [InlineData("EI12345", "Player ID is too short (expected EI + 16 digits)")]
    [InlineData("EI12345678901234567890", "Player ID is too long (expected EI + 16 digits)")]
    [InlineData("EI123456789012345X", "Player ID must be EI followed by exactly 16 digits")]
    [InlineData("EI1234567890123456", "")]
    public void EidProblemMatchesVueRules(string normalized, string expected) =>
        Assert.Equal(expected, LedgerFormatting.EidProblem(normalized));

    [Theory]
    [InlineData("EI1234567890123456", true)]
    [InlineData("", false)]
    [InlineData("EI123", false)]
    public void IsEidValidMatchesVue(string normalized, bool valid) =>
        Assert.Equal(valid, LedgerFormatting.IsEidValid(normalized));
}
