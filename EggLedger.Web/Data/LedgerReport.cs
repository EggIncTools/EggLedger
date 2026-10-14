using EggIdentity.Reports;

namespace EggLedger.Web.Data;

public sealed record LedgerReport(ReportDefinition Definition, ReportPlacement Placement) {
    public string AccountId { get; init; } = "";
    public long CreatedAt { get; init; }
    public long UpdatedAt { get; init; }

    public string Id => Definition.Id;
}
