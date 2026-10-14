using EggIdentity.Reports;

namespace EggLedger.Web.Data;

public interface IReportRunner {
    Task<ReportCube> RunReportAsync(ReportQuery query, string accountId, CancellationToken ct = default);
}
