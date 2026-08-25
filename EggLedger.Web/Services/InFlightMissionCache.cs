using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Services;

public sealed class InFlightMissionCache {
    private readonly Dictionary<string, IReadOnlyList<DatabaseMission>> _byAccount = new(StringComparer.Ordinal);

    public event Action? Changed;

    public void SetForAccount(string accountId, IReadOnlyList<DatabaseMission> missions) {
        _byAccount[accountId] = missions;
        Changed?.Invoke();
    }

    public IReadOnlyList<DatabaseMission> GetForAccount(string? accountId) =>
        accountId is not null && _byAccount.TryGetValue(accountId, out var missions)
            ? missions
            : Array.Empty<DatabaseMission>();
}
