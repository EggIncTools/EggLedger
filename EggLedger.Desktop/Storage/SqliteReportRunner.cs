using System.Globalization;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;

namespace EggLedger.Desktop.Storage;

public sealed class SqliteReportRunner(IReportSourceCache sources, IWeightData weights) : IReportRunner {
    private readonly IReportSourceCache _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IWeightData _weights = weights ?? throw new ArgumentNullException(nameof(weights));

    public async Task<ReportResult> RunReportAsync(ReportDefinition def, string accountId) {
        if (def.AccountId != accountId) {
            def.AccountId = accountId;
        }

        var source = await _sources.GetAsync(accountId).ConfigureAwait(false);
        var runner = new InMemoryReportRunner(_weights);
        return runner.Run(def, source.Missions, source.Drops, source.Fuel);
    }
}

public static class SqliteReportSource {
    private const string MissionSql =
        "SELECT player_id, mission_id, ship, duration_type, level, target, mission_type, "
        + "start_timestamp, return_timestamp, capacity, nominal_capacity, is_dub_cap, is_bugged_cap "
        + "FROM mission WHERE player_id = ? ORDER BY player_id, mission_id";

    private const string DropsSql =
        "SELECT player_id, mission_id, drop_index, artifact_id, spec_type, level, rarity, quality "
        + "FROM artifact_drops WHERE player_id = ? ORDER BY id";

    private const string FuelSql =
        "SELECT player_id, mission_id, egg_id, amount FROM mission_fuel WHERE player_id = ? ORDER BY id";

    public static Func<string, Task<ReportSource>> Loader(SqliteMissionDb db, IMissionStore missionStore) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(missionStore);
        return accountId => LoadAsync(db, missionStore, accountId);
    }

    public static async Task<ReportSource> LoadAsync(SqliteMissionDb db, IMissionStore missionStore, string accountId) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(missionStore);
        await missionStore.EnsureFilterColsBackfilledAsync(accountId).ConfigureAwait(false);
        return await Task.Run(() => Load(db, accountId)).ConfigureAwait(false);
    }

    public static ReportSource Load(SqliteMissionDb db, string accountId) {
        ArgumentNullException.ThrowIfNull(db);
        object?[] args = [accountId];

        var missionRows = db.Query(MissionSql, args);
        var missions = new List<MissionRowData>(missionRows.Count);
        foreach (var r in missionRows) {
            missions.Add(new MissionRowData {
                PlayerId = AsString(r[0]),
                MissionId = AsString(r[1]),
                Ship = AsLong(r[2]),
                DurationType = AsLong(r[3]),
                Level = AsLong(r[4]),
                Target = AsLong(r[5]),
                MissionType = AsLong(r[6]),
                StartTimestamp = AsLong(r[7]),
                ReturnTimestamp = AsLong(r[8]),
                Capacity = AsLong(r[9]),
                NominalCapacity = AsLong(r[10]),
                IsDubCap = AsBool(r[11]),
                IsBuggedCap = AsBool(r[12]),
            });
        }

        var dropRows = db.Query(DropsSql, args);
        var drops = new List<ArtifactDropRowData>(dropRows.Count);
        foreach (var r in dropRows) {
            drops.Add(new ArtifactDropRowData {
                PlayerId = AsString(r[0]),
                MissionId = AsString(r[1]),
                DropIndex = AsLong(r[2]),
                ArtifactId = AsLong(r[3]),
                SpecType = AsString(r[4]),
                Level = AsLong(r[5]),
                Rarity = AsLong(r[6]),
                Quality = AsDouble(r[7]),
            });
        }

        var fuelRows = db.Query(FuelSql, args);
        var fuel = new List<FuelRowData>(fuelRows.Count);
        foreach (var r in fuelRows) {
            fuel.Add(new FuelRowData {
                PlayerId = AsString(r[0]),
                MissionId = AsString(r[1]),
                EggId = AsLong(r[2]),
                Amount = AsDouble(r[3]),
            });
        }

        return new ReportSource(missions, drops, fuel);
    }

    private static string AsString(object? v) => v switch {
        null => "",
        string s => s,
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private static long AsLong(object? v) => v switch {
        null => 0,
        long l => l,
        int i => i,
        double d => (long)d,
        string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0,
        _ => Convert.ToInt64(v, CultureInfo.InvariantCulture),
    };

    private static double AsDouble(object? v) => v switch {
        null => 0,
        double d => d,
        long l => l,
        int i => i,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0,
        _ => Convert.ToDouble(v, CultureInfo.InvariantCulture),
    };

    private static bool AsBool(object? v) => v switch {
        null => false,
        bool b => b,
        _ => AsLong(v) != 0,
    };
}
