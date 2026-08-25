using EggLedger.Domain.MissionPacking;
using EggLedger.Web.Missions;

namespace EggLedger.Web.Tests.Missions;

public sealed class MissionDateBucketingTests {
    private static DateTime FakeLedgerDate(long encoded) {
        int y = (int)(encoded / 10000);
        int mo = (int)(encoded / 100 % 100);
        int d = (int)(encoded % 100);
        return new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Local);
    }

    private static DatabaseMission M(long encoded, string id) =>
        new() { LaunchDT = encoded, MissiondId = id };

    [Fact]
    public void ByDay_GroupsMissionsOnSameDayTogether() {
        var missions = new[] {
            M(20240315, "a"),
            M(20240315, "b"),
            M(20240316, "c"),
        };

        var result = MissionDateBucketing.ByDay(missions, FakeLedgerDate);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[new DateOnly(2024, 3, 15)].Count);
        Assert.Single(result[new DateOnly(2024, 3, 16)]);
    }

    [Fact]
    public void ByDay_PreservesInputOrderWithinADay() {
        var missions = new[] {
            M(20240315, "a"),
            M(20240315, "b"),
        };

        var result = MissionDateBucketing.ByDay(missions, FakeLedgerDate);

        Assert.Equal(["a", "b"], result[new DateOnly(2024, 3, 15)].Select(m => m.MissiondId));
    }

    [Fact]
    public void ByDay_EmptyInput_ReturnsEmpty() {
        var result = MissionDateBucketing.ByDay(Array.Empty<DatabaseMission>(), FakeLedgerDate);

        Assert.Empty(result);
    }
}
