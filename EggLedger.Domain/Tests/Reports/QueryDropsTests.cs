using EggIdentity.Filters;
using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;

public class QueryDropsTests {
    public static IEnumerable<object?[]> Cases() => new[] {
        new object?[] {
            new FilterCondition("drops", "c", "12_3_2_4.5"),
            "EXISTS (SELECT 1 FROM artifact_drops WHERE mission_id = m.mission_id AND player_id = m.player_id AND artifact_id = ? AND level = ? AND rarity = ?)",
            new object?[] { "12", "3", "2" },
            false,
        },
        [
            new FilterCondition("drops", "c", "%_%_3_%"),
            "EXISTS (SELECT 1 FROM artifact_drops WHERE mission_id = m.mission_id AND player_id = m.player_id AND rarity = ?)",
            new object?[] { "3" },
            false,
        ],
        [
            new FilterCondition("drops", "dnc", "12_%_%_%"),
            "NOT EXISTS (SELECT 1 FROM artifact_drops WHERE mission_id = m.mission_id AND player_id = m.player_id AND artifact_id = ?)",
            new object?[] { "12" },
            false,
        ],
        [
            new FilterCondition("drops", "c", "%_%_%_%"),
            "EXISTS (SELECT 1 FROM artifact_drops WHERE mission_id = m.mission_id AND player_id = m.player_id)",
            Array.Empty<object?>(),
            false,
        ],
        [
            new FilterCondition("drops", "c", ""),
            null, null, true,
        ],
        [
            new FilterCondition("drops", "x", "12_%_%_%"),
            null, null, true,
        ],
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConditionToSql_Drops(FilterCondition cond, string? wantSub, object?[]? wantArgs, bool wantEmpty) {
        var (clause, args) = QueryBuilder.ConditionToSql(cond);
        if (wantEmpty) {
            Assert.Equal("", clause);
            return;
        }
        Assert.Equal(wantSub, clause);
        Assert.Equal(wantArgs!.Length, args.Count);
        for (var i = 0; i < args.Count; i++) {
            Assert.Equal(wantArgs[i], args[i]);
        }
    }
}
