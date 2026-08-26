using System.Globalization;
using EggLedger.Desktop.Storage;
using Microsoft.Data.Sqlite;

namespace EggLedger.Desktop.Tests;

public sealed class MigrationTests {
    private static SqliteConnection FreshConnection() {


        var name = "migtest_" + Guid.NewGuid().ToString("N");
        var conn = new SqliteConnection($"Data Source={name};Mode=Memory;Cache=Shared");
        conn.Open();
        return conn;
    }

    private static int UserVersion(SqliteConnection conn) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool TableExists(SqliteConnection conn, string table) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n;";
        cmd.Parameters.AddWithValue("@n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static HashSet<string> Columns(SqliteConnection conn, string table) {
        var cols = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            cols.Add(reader.GetString(1));
        }
        return cols;
    }

    [Fact]
    public void MissionDb_FreshMigratesToV10() {
        using var conn = FreshConnection();
        SqliteMigrationRunner.MigrateMissionDb(conn);

        Assert.Equal(10, UserVersion(conn));
        Assert.True(TableExists(conn, "mission"));
        Assert.True(TableExists(conn, "backup"));
        Assert.True(TableExists(conn, "settings"));
        Assert.True(TableExists(conn, "artifact_drops"));
        Assert.True(TableExists(conn, "inflight_mission"));

        var inFlightCols = Columns(conn, "inflight_mission");
        foreach (var expected in new[] { "player_id", "mission_id", "captured_at", "payload" }) {
            Assert.Contains(expected, inFlightCols);
        }

        var missionCols = Columns(conn, "mission");
        foreach (var expected in new[]
        {
            "player_id", "mission_id", "start_timestamp", "complete_payload", "mission_type",
            "ship", "duration_type", "level", "capacity", "is_dub_cap", "is_bugged_cap",
            "target", "return_timestamp", "nominal_capacity",
        }) {
            Assert.Contains(expected, missionCols);
        }
    }

    [Fact]
    public void ReportDb_FreshMigratesToV12() {
        using var conn = FreshConnection();
        SqliteMigrationRunner.MigrateReportDb(conn);

        Assert.Equal(12, UserVersion(conn));
        Assert.True(TableExists(conn, "reports"));
        Assert.True(TableExists(conn, "report_groups"));

        var reportCols = Columns(conn, "reports");
        foreach (var expected in new[]
        {
            "id", "account_id", "name", "subject", "mode", "display_mode", "group_by",
            "filters", "color", "description", "chart_type", "value_filter_op",
            "group_id", "normalize_by", "label_colors", "secondary_group_by",
            "unfilled_color", "family_weight", "menno_enabled", "menno_compare_mode",
            "min_sample_size",
        }) {
            Assert.Contains(expected, reportCols);
        }
    }

    [Fact]
    public void MissionDb_RerunIsNoOp() {
        using var conn = FreshConnection();
        SqliteMigrationRunner.MigrateMissionDb(conn);
        Assert.Equal(10, UserVersion(conn));



        SqliteMigrationRunner.MigrateMissionDb(conn);
        SqliteMigrationRunner.MigrateMissionDb(conn);
        Assert.Equal(10, UserVersion(conn));
    }

    [Fact]
    public void ReportDb_RerunIsNoOp() {
        using var conn = FreshConnection();
        SqliteMigrationRunner.MigrateReportDb(conn);
        Assert.Equal(12, UserVersion(conn));

        SqliteMigrationRunner.MigrateReportDb(conn);
        Assert.Equal(12, UserVersion(conn));
    }

    [Fact]
    public void OpenMissionDb_AppliesPragmasAndMigrates() {
        var path = Path.Combine(Path.GetTempPath(), "egl_migtest_" + Guid.NewGuid().ToString("N"), "ledger.db");
        var dir = Path.GetDirectoryName(path)!;
        try {
            using var db = SqliteDatabase.OpenMissionDb(path);
            Assert.Equal(10, UserVersion(db.Connection));

            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
        } finally {


            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
