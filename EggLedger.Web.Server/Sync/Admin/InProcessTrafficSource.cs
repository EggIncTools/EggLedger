using EggIdentity.Contract;
using EggIdentity.Metrics;

namespace EggLedger.Web.Server.Sync.Admin;

public sealed class InProcessTrafficSource(TrafficReporter reporter) : ITrafficSource {
    public Task<TrafficSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(reporter.Snapshot());
}
