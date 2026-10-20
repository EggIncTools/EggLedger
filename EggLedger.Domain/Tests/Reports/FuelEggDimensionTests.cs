using EggIdentity.Filters;
using EggIdentity.Reports;
using EggLedger.Domain.Reports;
using Ei;
using Xunit;

namespace EggLedger.Domain.Tests.Reports;

public sealed class FuelEggDimensionTests {
    private static readonly MissionRowData Mission = new() {
        PlayerId = "p",
        MissionId = "m1",
        Ship = (long)MissionInfo.Spaceship.Henerprise,
        DurationType = (long)MissionInfo.DurationType.Epic,
    };

    [Fact]
    public void FuelSourceExposesEggDimensionWithDisplayNames() {
        var dim = LedgerReportSources.Fuel.Dimension(LedgerReportKeys.FuelEgg);

        Assert.NotNull(dim);
        Assert.Contains(dim.Field.Options, o => o.Value == ((long)Egg.DarkMatter).ToString() && o.Label == "Dark Matter");
    }

    [Fact]
    public void RecordedFuelRowsMapEggId() {
        var data = new LedgerReportData([Mission], [], [
            new FuelRowData { PlayerId = "p", MissionId = "m1", EggId = (long)Egg.Antimatter, Amount = 5e12 },
            new FuelRowData { PlayerId = "p", MissionId = "m1", EggId = (long)Egg.DarkMatter, Amount = 2e12 },
        ]);

        var eggs = LedgerReportSources.FuelRows(data)
            .Select(r => LedgerReportSources.Fuel.Value(r, LedgerReportKeys.FuelEgg))
            .ToList();

        Assert.Equal([FilterSubjectValue.Of((double)Egg.Antimatter), FilterSubjectValue.Of((double)Egg.DarkMatter)], eggs);
    }

    [Theory]
    [InlineData(34192000000000000d, "34.2q")]
    [InlineData(5e12, "5T")]
    [InlineData(812, "812")]
    public void FuelAmountDefaultsToCompact(double value, string expected) {
        var format = LedgerReportSources.Fuel.Measure(LedgerReportKeys.FuelAmount)!.Format;

        Assert.Equal(ValueFormatKind.Compact, format.Kind);
        Assert.Equal(expected, format.Format(value));
    }

    [Fact]
    public void FullOverrideExpandsFuelAmount() {
        var format = LedgerReportSources.Fuel.Measure(LedgerReportKeys.FuelAmount)!.Format;

        Assert.Equal(ValueFormatKind.Integer, new ReportView(ReportCharts.Bar) { NumberFormat = ReportNumberFormat.Full }.Resolve(format).Kind);
    }

    [Fact]
    public void SynthesizedFuelRowsCarryTableEggs() {
        var data = new LedgerReportData([Mission], [], []);

        var rows = LedgerReportSources.FuelRows(data);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.NotEqual(0, r.Fuel.EggId));
    }
}
