using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace EggLedger.Desktop.Storage;

public static class SqliteMigrationRunner {
    public const int MissionTargetVersion = 10;

    public const int ReportTargetVersion = 12;

    private static readonly Regex FileNamePattern =
        new(@"\.Migrations\.(?<set>[^.]+)\.(?<num>\d+)_", RegexOptions.Compiled);

    public static void MigrateMissionDb(SqliteConnection connection) =>
        Migrate(connection, "Mission", MissionTargetVersion);

    public static void MigrateReportDb(SqliteConnection connection) =>
        Migrate(connection, "Report", ReportTargetVersion);

    public static void Migrate(SqliteConnection connection, string set, int targetVersion) {
        var migrations = LoadMigrations(set);
        int current = GetUserVersion(connection);

        foreach (var (version, sql) in migrations) {
            if (version <= current || version > targetVersion) {
                continue;
            }
            ApplyOne(connection, version, sql);
            current = version;
        }
    }

    private static void ApplyOne(SqliteConnection connection, int version, string sql) {
        using var tx = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand()) {
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }


        using (var setVersion = connection.CreateCommand()) {
            setVersion.Transaction = tx;
            setVersion.CommandText = string.Format(
                CultureInfo.InvariantCulture, "PRAGMA user_version = {0};", version);
            setVersion.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static int GetUserVersion(SqliteConnection connection) {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = cmd.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static List<(int Version, string Sql)> LoadMigrations(string set) {
        var assembly = typeof(SqliteMigrationRunner).Assembly;
        var prefix = $".Migrations.{set}.";
        var result = new List<(int, string)>();

        foreach (var name in assembly.GetManifestResourceNames()) {
            if (!name.Contains(prefix, StringComparison.Ordinal) || !name.EndsWith(".up.sql", StringComparison.Ordinal)) {
                continue;
            }
            var match = FileNamePattern.Match(name);
            if (!match.Success || !string.Equals(match.Groups["set"].Value, set, StringComparison.Ordinal)) {
                continue;
            }
            int version = int.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture);
            result.Add((version, ReadResource(assembly, name)));
        }

        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    private static string ReadResource(Assembly assembly, string name) {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded migration {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
