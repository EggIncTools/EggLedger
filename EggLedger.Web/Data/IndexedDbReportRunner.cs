using EggIdentity.Reports;
using EggLedger.Domain.Reports;

namespace EggLedger.Web.Data;

public sealed class IndexedDbReportRunner(IReportSourceCache sources, IWeightData weights, TimeProvider? time = null) : IReportRunner {
    private readonly IReportSourceCache _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IWeightData _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<ReportCube> RunReportAsync(ReportQuery query, string accountId, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(query);
        var registry = LedgerReportSources.Registry(async _ => await _sources.GetAsync(accountId).ConfigureAwait(false), _weights, _time);
        return registry.RunAsync(LedgerReportQueries.Prepare(query), ReportContext.Utc, ct);
    }
}
