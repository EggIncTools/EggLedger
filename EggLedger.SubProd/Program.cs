using EggLedger.SubProd;
using EggLedger.Web.Server.SubProd;
using Npgsql;

if (args.Length == 0) {
    Console.Error.WriteLine("usage: EggLedger.SubProd <doctor|sanitize|verify|run>");
    return 1;
}

return args[0] switch {
    "doctor" => await Doctor.RunAsync(),
    "sanitize" => await Sanitize.RunAsync(),
    "verify" => await Verify.RunAsync(),
    "run" => await Run.RunAsync(),
    _ => Unknown(args[0]),
};

static int Unknown(string verb) {
    Console.Error.WriteLine($"eggledger.subprod: unknown verb '{verb}'. usage: doctor|sanitize|verify|run");
    return 1;
}

namespace EggLedger.SubProd {
    public static class Doctor {
        public static Task<int> RunAsync() {
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "";
            Console.WriteLine($"eggledger.subprod doctor: environment={SubProdFence.RequiredEnvironment}");

            if (string.IsNullOrEmpty(databaseUrl)) {
                Console.WriteLine("DATABASE_URL: not set");
            } else {
                var builder = new NpgsqlConnectionStringBuilder(databaseUrl);
                Console.WriteLine($"DATABASE_URL: host={builder.Host} database={builder.Database}");
                var ok = string.Equals(builder.Database, SubProdFence.SubProdDatabaseName, StringComparison.Ordinal);
                Console.WriteLine(ok
                    ? $"database name: OK, matches '{SubProdFence.SubProdDatabaseName}'"
                    : $"database name: MISMATCH, expected '{SubProdFence.SubProdDatabaseName}', got '{builder.Database}'");
            }

            SubProdFence.WrapGetter(Environment.GetEnvironmentVariable, SubProdFence.RequiredEnvironment, out var report);
            Console.WriteLine("fence report (as if ASPNETCORE_ENVIRONMENT=Staging):");
            foreach (var entry in report) {
                Console.WriteLine($"  {entry.Key}: {(entry.Forced ? "forced empty" : "allowed through")}");
            }

            return Task.FromResult(0);
        }
    }
}
