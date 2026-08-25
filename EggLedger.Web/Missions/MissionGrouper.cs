using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Missions;

public sealed class GroupedYear {
    public int Year { get; set; }
    public bool Enabled { get; set; }
}

public sealed class GroupedMonth {
    public int Month { get; set; }
    public bool Enabled { get; set; }
}

public sealed class GroupedDay {
    public int Day { get; set; }
    public bool Enabled { get; set; }
}

public sealed class GroupedArrays {
    public List<GroupedYear> Year { get; set; } = [];
    public List<List<GroupedMonth>> Month { get; set; } = [];
    public List<List<List<GroupedDay>>> Day { get; set; } = [];
}

public sealed class MissionGrouping {
    public List<List<List<List<DatabaseMission>>>> Missions { get; init; } = [];
    public GroupedArrays Arrays { get; init; } = new();
    public bool AllVisible { get; init; }
}

public static class MissionGrouper {
    public static MissionGrouping Group(
        IReadOnlyList<DatabaseMission>? missions,
        Func<long, DateTime> ledgerDate,
        bool collapseOlderSections) {
        if (missions is null || missions.Count == 0) {
            return new MissionGrouping { AllVisible = true };
        }

        var byDay = MissionDateBucketing.ByDay(missions, ledgerDate);

        var dateMap = new Dictionary<int, Dictionary<int, Dictionary<int, List<DatabaseMission>>>>();
        foreach (var (day, dayMissions) in byDay) {
            if (!dateMap.TryGetValue(day.Year, out var ym)) {
                ym = [];
                dateMap[day.Year] = ym;
            }
            if (!ym.TryGetValue(day.Month, out var md)) {
                md = [];
                ym[day.Month] = md;
            }
            md[day.Day] = dayMissions;
        }

        var uniqueYears = new List<int>(dateMap.Keys);
        uniqueYears.Sort((a, b) => b - a);

        var arrays = new GroupedArrays();
        var matrix = new List<List<List<List<DatabaseMission>>>>();

        for (int yi = 0; yi < uniqueYears.Count; yi++) {
            int y = uniqueYears[yi];
            bool yearEnabled = !collapseOlderSections || yi == 0;
            arrays.Year.Add(new GroupedYear { Year = y, Enabled = yearEnabled });

            var months = new List<int>(dateMap[y].Keys);
            months.Sort((a, b) => b - a);

            var monthStates = new List<GroupedMonth>();
            var dayStatesForYear = new List<List<GroupedDay>>();
            var yearMatrix = new List<List<List<DatabaseMission>>>();

            foreach (int mo in months) {
                monthStates.Add(new GroupedMonth { Month = mo, Enabled = yearEnabled });

                var days = new List<int>(dateMap[y][mo].Keys);
                days.Sort((a, b) => b - a);

                var dayStates = new List<GroupedDay>();
                var monthMatrix = new List<List<DatabaseMission>>();
                foreach (int da in days) {
                    dayStates.Add(new GroupedDay { Day = da, Enabled = yearEnabled });
                    var dayMissions = new List<DatabaseMission>(dateMap[y][mo][da]);
                    dayMissions.Reverse();
                    monthMatrix.Add(dayMissions);
                }
                dayStatesForYear.Add(dayStates);
                yearMatrix.Add(monthMatrix);
            }

            arrays.Month.Add(monthStates);
            arrays.Day.Add(dayStatesForYear);
            matrix.Add(yearMatrix);
        }

        bool allVisible = !collapseOlderSections || uniqueYears.Count <= 1;

        return new MissionGrouping {
            Missions = matrix,
            Arrays = arrays,
            AllVisible = allVisible,
        };
    }
}
