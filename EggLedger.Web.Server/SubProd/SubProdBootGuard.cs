using Npgsql;

namespace EggLedger.Web.Server.SubProd;

public static class SubProdBootGuard {
    public static void EnsureSubProdDatabase(string connectionString) {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (!string.Equals(database, SubProdFence.SubProdDatabaseName, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Refusing to proceed: DATABASE_URL targets database '{database}', not '{SubProdFence.SubProdDatabaseName}'.");
        }
    }
}
