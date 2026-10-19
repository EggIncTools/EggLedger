using EggIdentity.Reports;
using EggIdentity.Reports.Browser;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.State;

namespace EggLedger.Web.Server.Reports;

public sealed class BrowserReportRunner : IReportRunner, IDisposable {
    private readonly BrowserReportRegistry _reports;
    private readonly IndexedDbReportRunner _server;
    private readonly LedgerDataHub _hub;

    public BrowserReportRunner(BrowserReportRegistry reports, IndexedDbReportRunner server, LedgerDataHub hub) {
        _reports = reports;
        _server = server;
        _hub = hub;
        _hub.AccountInvalidated += OnAccountInvalidated;
    }

    public async Task<ReportCube> RunReportAsync(ReportQuery query, string accountId, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(query);
        if (!_reports.BrowserActive) return await _server.RunReportAsync(query, accountId, ct).ConfigureAwait(false);
        try {
            var tier = await _reports.ReadyAsync(ct).ConfigureAwait(false);
            var context = ReportContext.Utc with { Tier = tier, Partition = accountId };
            return await _reports.Registry.RunAsync(LedgerReportQueries.Prepare(query), context, ct).ConfigureAwait(false);
        } catch (Exception) when (!_reports.BrowserActive && !ct.IsCancellationRequested) {
            return await _server.RunReportAsync(query, accountId, ct).ConfigureAwait(false);
        }
    }

    private void OnAccountInvalidated(string accountId) => _ = _reports.InvalidateAsync(partition: accountId).AsTask();

    public void Dispose() => _hub.AccountInvalidated -= OnAccountInvalidated;
}
