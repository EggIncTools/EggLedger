using EggLedger.Domain.MissionPacking;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.State;

namespace EggLedger.Web.Tests.Data;

public static class TestHub {
    public static LedgerReportData SourceFor(string accountId) => new(
        [new MissionRowData { PlayerId = accountId, MissionId = accountId + "-m1", StartTimestamp = 1, ReturnTimestamp = 2 }],
        [new ArtifactDropRowData { PlayerId = accountId, MissionId = accountId + "-m1" }],
        []);

    public static Task<LedgerReportData> SourceOf(string accountId) => Task.FromResult(SourceFor(accountId));

    public static LedgerDataHub New(
        Func<string, Task<LedgerReportData>>? loader = null,
        LedgerDataStore? store = null,
        TimeProvider? clock = null,
        Func<string, Task<IReadOnlyList<DatabaseMission>?>>? inFlight = null) {
        var packer = new MissionPacker(EiafxMissionConfigSource.Instance);
        var queries = new MissionQueryHandlers(new FakeMissionStore(), EiafxQualityAdapter.Instance, packer);
        return new LedgerDataHub(
            store ?? new LedgerDataStore(clock ?? TestTime.New()),
            new LocalDataOwner(),
            loader ?? SourceOf,
            packer,
            queries,
            inFlight);
    }
}
