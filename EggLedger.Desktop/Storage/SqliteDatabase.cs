using Microsoft.Data.Sqlite;

namespace EggLedger.Desktop.Storage;

public sealed class SqliteDatabase : IDisposable {
    private SqliteDatabase(SqliteConnection connection) {
        Connection = connection;
    }

    public SqliteConnection Connection { get; }

    public Lock Gate { get; } = new();

    public static SqliteDatabase OpenMissionDb(string path) {
        var db = Open(path, isConnectionString: false);
        SqliteMigrationRunner.MigrateMissionDb(db.Connection);
        return db;
    }

    public static SqliteDatabase OpenReportDb(string path) {
        var db = Open(path, isConnectionString: false);
        SqliteMigrationRunner.MigrateReportDb(db.Connection);
        return db;
    }

    public static SqliteDatabase Open(string path, bool isConnectionString = false) {
        if (!isConnectionString && path != ":memory:") {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }
        }

        string connectionString;
        if (isConnectionString) {
            connectionString = path;
        } else {
            var builder = new SqliteConnectionStringBuilder {
                DataSource = path,


                Cache = path == ":memory:" ? SqliteCacheMode.Shared : SqliteCacheMode.Default,
            };
            connectionString = builder.ConnectionString;
        }

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        ApplyPragmas(connection, path, isConnectionString);
        return new SqliteDatabase(connection);
    }

    private static void ApplyPragmas(SqliteConnection connection, string path, bool isConnectionString) {
        using var cmd = connection.CreateCommand();


        var isMemory = isConnectionString
            ? path.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase)
            : path == ":memory:";
        var journal = isMemory ? "" : "PRAGMA journal_mode=WAL;";
        cmd.CommandText = $"PRAGMA foreign_keys=ON;{journal}PRAGMA busy_timeout=10000;";
        cmd.ExecuteNonQuery();
    }

    public void Dispose() {
        Connection.Dispose();
    }
}
