using EggLedger.Domain.Reports;

namespace EggLedger.Web.Data;

public sealed class IndexedDbReportRunner(IReportSourceCache sources, IWeightData weights) : IReportRunner {
    private readonly IReportSourceCache _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IWeightData _weights = weights ?? throw new ArgumentNullException(nameof(weights));

    public async Task<ReportResult> RunReportAsync(ReportDefinition def, string accountId) {
        var source = await _sources.GetAsync(accountId).ConfigureAwait(false);
        var runner = new InMemoryReportRunner(_weights);
        return runner.Run(def, source.Missions, source.Drops, source.Fuel);
    }
}
