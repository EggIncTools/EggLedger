using System.Globalization;
using EggLedger.Domain.Reports;

namespace EggLedger.Domain.Tests.Reports;


public class WeightTests {
    private static string Ago(int days) =>
        DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void ClassifyWeight_2D_NoArtifact_IsLow() {
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            SecondaryGroupBy = "duration_type",
        };
        Assert.Equal("LOW", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_2D_ArtifactNoDate_IsHeavy() {
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            SecondaryGroupBy = "artifact_name",
        };
        Assert.Equal("HEAVY", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_2D_ArtifactWithDate_IsMedium() {
        var def = new ReportDefinition {
            Mode = "aggregate",
            GroupBy = "ship_type",
            SecondaryGroupBy = "artifact_name",
            Filters = new ReportFilters {
                And = [new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(30) }],
            },
        };
        Assert.Equal("MEDIUM", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_TimePivot_NoArtifact_NoDate_IsHeavy() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "ship_type",
            TimeBucket = "month",
        };
        Assert.Equal("HEAVY", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_TimePivot_NoArtifact_WithRecentDate_IsMedium() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "ship_type",
            TimeBucket = "month",
            Filters = new ReportFilters {
                And = [new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(30) }],
            },
        };
        Assert.Equal("MEDIUM", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_TimePivot_NoArtifact_WithOldDate_IsHeavy() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "ship_type",
            TimeBucket = "month",
            Filters = new ReportFilters {
                And = [new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(180) }],
            },
        };
        Assert.Equal("HEAVY", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_TimePivot_ArtifactSecondary_NoDate_IsHeavy() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "artifact_name",
            TimeBucket = "month",
        };
        Assert.Equal("HEAVY", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void ClassifyWeight_TimePivot_ArtifactSecondary_WithDate_IsMedium() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            SecondaryGroupBy = "artifact_name",
            TimeBucket = "month",
            Filters = new ReportFilters {
                And = [new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(30) }],
            },
        };
        Assert.Equal("MEDIUM", Weight.ClassifyWeight(def));
    }


    [Fact]
    public void DateWindow_NoFilter_TreatedAsLarge_TimeSeriesHeavy() {
        var def = new ReportDefinition { Mode = "time_series", GroupBy = "time_bucket", TimeBucket = "month" };
        Assert.Equal("HEAVY", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void DateWindow_RecentLaunch_TimeSeriesMedium() {
        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            TimeBucket = "month",
            Filters = new ReportFilters {
                And = [new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(30) }],
            },
        };
        Assert.Equal("MEDIUM", Weight.ClassifyWeight(def));
    }

    [Fact]
    public void DateWindow_PicksMostRestrictive_TimeSeriesMedium() {

        var def = new ReportDefinition {
            Mode = "time_series",
            GroupBy = "time_bucket",
            TimeBucket = "month",
            Filters = new ReportFilters {
                And = [
                    new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(90) },
                    new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = Ago(10) },
                ],
            },
        };
        Assert.Equal("MEDIUM", Weight.ClassifyWeight(def));
    }
}
