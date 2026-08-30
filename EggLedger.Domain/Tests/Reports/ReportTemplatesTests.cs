using EggLedger.Domain.Reports;
using Xunit;

namespace EggLedger.Domain.Tests.Reports;

public sealed class ReportTemplatesTests {
    [Fact]
    public void All_HaveUniqueStableIds() {
        var ids = ReportTemplates.All.Select(d => d.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        Assert.All(ids, id => Assert.StartsWith("tmpl_", id));
    }

    [Fact]
    public void Find_ReturnsMatchingTemplate() {
        var found = ReportTemplates.Find(ReportTemplates.TotalFuelByShip);
        Assert.NotNull(found);
        Assert.Equal("fuel_eggs", found!.Subject);
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull() {
        Assert.Null(ReportTemplates.Find("tmpl_does_not_exist"));
    }
}
