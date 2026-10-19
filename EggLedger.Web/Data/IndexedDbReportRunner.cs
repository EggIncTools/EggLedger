using EggIdentity.Reports;
using EggLedger.Domain.Reports;

namespace EggLedger.Web.Data;

public sealed class IndexedDbReportRunner(IReportSourceCache sources, IWeightData weights, TimeProvider? time = null) : IReportRunner, IDisposable {
    private readonly IReportSourceCache _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IWeightData _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(2, 2);

    public async Task<ReportCube> RunReportAsync(ReportQuery query, string accountId, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(query);
        var registry = LedgerReportSources.Registry(async _ => await _sources.GetAsync(accountId).ConfigureAwait(false), _weights, _time);
        var prepared = LedgerReportQueries.Prepare(query);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            return await Task.Run(() => registry.RunAsync(prepared, ReportContext.Utc, ct), ct).ConfigureAwait(false);
        } finally {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
