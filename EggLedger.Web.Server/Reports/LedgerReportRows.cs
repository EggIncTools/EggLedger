using EggIdentity.Reports;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;

namespace EggLedger.Web.Server.Reports;

public sealed class LedgerReportRows(
    LedgerDataStore store,
    IDataOwner owner,
    Func<string, Task<LedgerReportData>> loader,
    AuthenticationStateProvider auth,
    IHttpContextAccessor http) {
    public async ValueTask<LedgerReportData> ReadAsync(string? partition) {
        ArgumentException.ThrowIfNullOrEmpty(partition);
        if (http.HttpContext?.User is { Identity.IsAuthenticated: true } user && auth is IHostEnvironmentAuthenticationStateProvider host) {
            host.SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        }
        var key = await owner.KeyAsync().ConfigureAwait(false);
        return await store.GetAsync(key, partition, () => loader(partition)).ConfigureAwait(false);
    }

    public ValueTask<string> VersionAsync(string? partition) {
        ArgumentException.ThrowIfNullOrEmpty(partition);
        return ValueTask.FromResult(store.Version(partition));
    }
}

public sealed class LedgerMissionRowProvider(LedgerReportRows rows) : IReportRowProvider<LedgerMissionRow> {
    public async ValueTask<IReadOnlyList<LedgerMissionRow>> ReadAsync(string? partition, CancellationToken ct) =>
        LedgerReportSources.MissionRows(await rows.ReadAsync(partition).ConfigureAwait(false));

    public ValueTask<string> VersionAsync(string? partition, CancellationToken ct) => rows.VersionAsync(partition);
}

public sealed class LedgerArtifactRowProvider(LedgerReportRows rows, IWeightData weights) : IReportRowProvider<LedgerArtifactRow> {
    public async ValueTask<IReadOnlyList<LedgerArtifactRow>> ReadAsync(string? partition, CancellationToken ct) =>
        LedgerReportSources.ArtifactRows(await rows.ReadAsync(partition).ConfigureAwait(false), weights);

    public ValueTask<string> VersionAsync(string? partition, CancellationToken ct) => rows.VersionAsync(partition);
}

public sealed class LedgerFuelRowProvider(LedgerReportRows rows) : IReportRowProvider<LedgerFuelRow> {
    public async ValueTask<IReadOnlyList<LedgerFuelRow>> ReadAsync(string? partition, CancellationToken ct) =>
        LedgerReportSources.FuelRows(await rows.ReadAsync(partition).ConfigureAwait(false));

    public ValueTask<string> VersionAsync(string? partition, CancellationToken ct) => rows.VersionAsync(partition);
}
