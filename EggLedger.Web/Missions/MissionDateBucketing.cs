using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Missions;

public static class MissionDateBucketing {
    public static Dictionary<DateOnly, List<DatabaseMission>> ByDay(
        IReadOnlyList<DatabaseMission> missions,
        Func<long, DateTime> ledgerDate) {
        var result = new Dictionary<DateOnly, List<DatabaseMission>>();
        foreach (var mission in missions) {
            var day = DateOnly.FromDateTime(ledgerDate(mission.LaunchDT));
            if (!result.TryGetValue(day, out var list)) {
                list = [];
                result[day] = list;
            }
            list.Add(mission);
        }
        return result;
    }
}
