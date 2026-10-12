using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Missions;
using Ei;

namespace EggLedger.Web.Tests.Missions;

public sealed class MissionFilterMatcherTests {
    private static DatabaseMission Mission(
        MissionInfo.Spaceship? ship = MissionInfo.Spaceship.ChickenOne,
        MissionInfo.DurationType? duration = MissionInfo.DurationType.Short,
        int level = 0,
        int targetInt = -1,
        int missionType = 0,
        bool dubCap = false,
        bool buggedCap = false,
        long launchDT = 0,
        long returnDT = 0,
        string id = "m1") => new() {
            Ship = ship,
            DurationType = duration,
            Level = level,
            TargetInt = targetInt,
            MissionType = missionType,
            IsDubCap = dubCap,
            IsBuggedCap = buggedCap,
            LaunchDT = launchDT,
            ReturnDT = returnDT,
            MissiondId = id,
        };

    private static MissionFilterMatcher Matcher(
        ShipDropsFetcher? fetcher = null,
        IReadOnlyList<PossibleMission>? configs = null) =>
        new(configs ?? [], "acct", fetcher ?? ((_, _) => Task.FromResult<IReadOnlyList<MissionDrop>?>(null)), TimeZoneInfo.Local);

    private static FilterCondition C(string top, string op, string val) => new(top, op, val);

    private static FilterSet Rows(IReadOnlyList<FilterCondition> and, params IReadOnlyList<FilterCondition>[] or) => new(and, or);

    [Theory]
    [InlineData(MissionInfo.Spaceship.ChickenOne, "=", "0", true)]
    [InlineData(MissionInfo.Spaceship.ChickenNine, "=", "0", false)]
    [InlineData(MissionInfo.Spaceship.ChickenOne, "!=", "0", false)]
    [InlineData(MissionInfo.Spaceship.ChickenNine, "!=", "0", true)]
    [InlineData(MissionInfo.Spaceship.ChickenHeavy, ">", "1", true)]
    [InlineData(MissionInfo.Spaceship.ChickenOne, ">", "1", false)]
    [InlineData(MissionInfo.Spaceship.ChickenOne, "<", "1", true)]
    [InlineData(MissionInfo.Spaceship.ChickenHeavy, "<", "1", false)]
    public async Task ShipField(MissionInfo.Spaceship ship, string op, string val, bool expected) {
        var m = Mission(ship: ship);
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("ship", op, val)));
    }

    [Theory]
    [InlineData(3, "=", "3", true)]
    [InlineData(3, "!=", "3", false)]
    [InlineData(5, ">", "3", true)]
    [InlineData(2, "<", "3", true)]
    [InlineData(5, "gte", "5", true)]
    public async Task LevelField(int level, string op, string val, bool expected) {
        var m = Mission(level: level);
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("level", op, val)));
    }

    [Theory]
    [InlineData(0, "=", "0", true)]
    [InlineData(1, "=", "0", false)]
    [InlineData(-1, "=", "-1", true)]
    public async Task TypeField(int missionType, string op, string val, bool expected) {
        var m = Mission(missionType: missionType);
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("type", op, val)));
    }

    [Fact]
    public async Task FarmAlias_MatchesMissionType() =>
        Assert.True(await Matcher().MatchesAsync(Mission(missionType: 1), C("farm", "=", "1")));

    [Theory]
    [InlineData(40, "=", "40", true)]
    [InlineData(-1, "=", "-1", true)]
    [InlineData(40, "!=", "41", true)]
    public async Task TargetField(int target, string op, string val, bool expected) {
        var m = Mission(targetInt: target);
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("target", op, val)));
    }

    [Theory]
    [InlineData(true, "true", true)]
    [InlineData(false, "true", false)]
    [InlineData(true, "false", false)]
    [InlineData(false, "false", true)]
    public async Task DubCapField(bool dub, string val, bool expected) {
        var m = Mission(dubCap: dub);
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("dubcap", "=", val)));
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("dubcap", val, "")));
    }

    [Theory]
    [InlineData(true, "true", true)]
    [InlineData(false, "false", true)]
    [InlineData(true, "false", false)]
    public async Task BuggedCapField(bool bugged, string val, bool expected) {
        var m = Mission(buggedCap: bugged);
        Assert.Equal(expected, await Matcher().MatchesAsync(m, C("buggedcap", "=", val)));
    }

    private static long Unix(int y, int mo, int d) {
        var dt = new DateTime(y, mo, d, 12, 0, 0, DateTimeKind.Local);
        return ((DateTimeOffset)dt).ToUnixTimeSeconds();
    }

    [Fact]
    public async Task LaunchDate_EqualsMatchesSameDay() {
        var m = Mission(launchDT: Unix(2024, 6, 1));
        Assert.True(await Matcher().MatchesAsync(m, C("launchDT", "d=", "2024-06-01")));
        var other = Mission(launchDT: Unix(2024, 6, 2));
        Assert.False(await Matcher().MatchesAsync(other, C("launchDT", "d=", "2024-06-01")));
    }

    [Fact]
    public async Task LaunchDate_AfterMatchesLaterMission() {
        var m = Mission(launchDT: Unix(2024, 6, 2));
        Assert.True(await Matcher().MatchesAsync(m, C("launchDT", ">", "2024-06-01")));
        var earlier = Mission(launchDT: Unix(2024, 5, 31));
        Assert.False(await Matcher().MatchesAsync(earlier, C("launchDT", ">", "2024-06-01")));
    }

    [Fact]
    public async Task LaunchDate_BeforeMatchesEarlierMission() {
        var m = Mission(launchDT: Unix(2024, 5, 31));
        Assert.True(await Matcher().MatchesAsync(m, C("launchDT", "<", "2024-06-01")));
    }

    [Fact]
    public async Task LaunchDate_OnOrBefore_IsInclusive() {
        var after = Mission(launchDT: Unix(2024, 12, 31));
        Assert.False(await Matcher().MatchesAsync(after, C("launchDT", "<=", "2024-01-01")));
        var onBoundary = Mission(launchDT: Unix(2024, 1, 1));
        Assert.True(await Matcher().MatchesAsync(onBoundary, C("launchDT", "<=", "2024-01-01")));
        var before = Mission(launchDT: Unix(2023, 12, 31));
        Assert.True(await Matcher().MatchesAsync(before, C("launchDT", "<=", "2024-01-01")));
    }

    [Fact]
    public async Task UnknownField_PassesThrough() =>
        Assert.True(await Matcher().MatchesAsync(Mission(), C("nonsense", "=", "1")));

    [Fact]
    public async Task EmptyValue_IsIgnored() =>
        Assert.True(await Matcher().MatchesAsync(Mission(level: 3), C("level", "=", "")));

    private static IReadOnlyList<PossibleMission> DropConfigs() => [
        new PossibleMission {
            Ship = MissionInfo.Spaceship.ChickenOne,
            Durations = [
                new() { DurationType = MissionInfo.DurationType.Short, MinQuality = 0, MaxQuality = 5, LevelQualityBump = 1 },
            ],
        },
    ];

    private static MissionDrop Drop(int id, int level, int rarity) =>
        new() { Id = id, Level = level, Rarity = rarity };

    private static MissionFilterMatcher DropsMatcher(params MissionDrop[] drops) =>
        Matcher((_, _) => Task.FromResult<IReadOnlyList<MissionDrop>?>(drops), DropConfigs());

    [Fact]
    public async Task Drops_Contains_FindsMatchingDrop() {
        var matcher = DropsMatcher(Drop(40, 2, 1));
        Assert.True(await matcher.MatchesAsync(Mission(), C("drops", "c", "40_2_1_3")));
        Assert.False(await matcher.MatchesAsync(Mission(), C("drops", "c", "41_2_1_3")));
    }

    [Fact]
    public async Task Drops_Contains_AnyRarityWildcard() {
        var matcher = DropsMatcher(Drop(40, 2, 1));
        Assert.True(await matcher.MatchesAsync(Mission(), C("drops", "c", "%_%_1_%")));
        Assert.False(await matcher.MatchesAsync(Mission(), C("drops", "c", "%_%_2_%")));
    }

    [Fact]
    public async Task Drops_DoesNotContain() {
        var matcher = DropsMatcher(Drop(40, 2, 1));
        Assert.False(await matcher.MatchesAsync(Mission(), C("drops", "dnc", "40_2_1_3")));
        Assert.True(await matcher.MatchesAsync(Mission(), C("drops", "dnc", "41_2_1_3")));
    }

    [Fact]
    public async Task Drops_NullFetch_Fails() {
        var matcher = Matcher((_, _) => Task.FromResult<IReadOnlyList<MissionDrop>?>(null), DropConfigs());
        Assert.False(await matcher.MatchesAsync(Mission(), C("drops", "c", "40_2_1_3")));
    }

    [Fact]
    public async Task Drops_MissingShipConfig_Fails() {
        var matcher = Matcher((_, _) => Task.FromResult<IReadOnlyList<MissionDrop>?>([]), []);
        Assert.False(await matcher.MatchesAsync(Mission(), C("drops", "c", "40_2_1_3")));
    }

    [Fact]
    public async Task Drops_QualityOutOfRange_Fails() =>
        Assert.False(await DropsMatcher(Drop(40, 2, 1)).MatchesAsync(Mission(), C("drops", "c", "40_2_1_9")));

    [Fact]
    public async Task CountMatchingDrops_CountsAllMatches() {
        var matcher = DropsMatcher(Drop(40, 2, 1), Drop(40, 2, 1), Drop(40, 3, 1));
        Assert.Equal(2, await matcher.CountMatchingDropsAsync(Mission(), DropMatch.Decode("40_2_1_3")));
    }

    [Fact]
    public async Task CountMatchingDrops_MissingShipConfig_ReturnsZero() {
        var matcher = Matcher((_, _) => Task.FromResult<IReadOnlyList<MissionDrop>?>([]), []);
        Assert.Equal(0, await matcher.CountMatchingDropsAsync(Mission(), DropMatch.Decode("40_2_1_3")));
    }

    [Fact]
    public async Task CountMatchingDrops_NullFetch_ReturnsZero() {
        var matcher = Matcher((_, _) => Task.FromResult<IReadOnlyList<MissionDrop>?>(null), DropConfigs());
        Assert.Equal(0, await matcher.CountMatchingDropsAsync(Mission(), DropMatch.Decode("40_2_1_3")));
    }

    [Fact]
    public async Task MissionMatches_AllAndConditionsMustPass() {
        var m = Mission(ship: MissionInfo.Spaceship.ChickenOne, level: 3);
        Assert.True(await Matcher().MatchesAsync(m, Rows([C("ship", "=", "0"), C("level", "=", "3")])));
        Assert.False(await Matcher().MatchesAsync(m, Rows([C("ship", "=", "0"), C("level", "=", "4")])));
    }

    [Fact]
    public async Task MissionMatches_OrSiblingRescuesFailingAnd() {
        var m = Mission(ship: MissionInfo.Spaceship.ChickenOne, level: 3);
        Assert.True(await Matcher().MatchesAsync(m, Rows([C("level", "=", "4")], [C("level", "=", "3")])));
    }

    [Fact]
    public async Task MissionMatches_OrSiblingAllFail_StillFails() {
        var m = Mission(level: 3);
        Assert.False(await Matcher().MatchesAsync(m, Rows([C("level", "=", "4")], [C("level", "=", "5")])));
    }

    [Fact]
    public async Task MissionMatches_EmptyFilters_AlwaysPasses() =>
        Assert.True(await Matcher().MatchesAsync(Mission(), FilterSet.Empty));
}
