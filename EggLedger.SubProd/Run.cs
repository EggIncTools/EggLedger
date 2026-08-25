using System.Diagnostics;
using EggLedger.Web.Server.SubProd;
using Npgsql;

namespace EggLedger.SubProd;

public static class Run {
    public static async Task<int> RunAsync() {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(databaseUrl)) {
            Console.Error.WriteLine("eggledger.subprod run: DATABASE_URL not set");
            return 1;
        }

        try {
            SubProdBootGuard.EnsureSubProdDatabase(databaseUrl);
        } catch (InvalidOperationException ex) {
            Console.Error.WriteLine($"eggledger.subprod run: {ex.Message}");
            return 1;
        }

        await using var source = NpgsqlDataSource.Create(databaseUrl);
        if (!await Verify.RunAsync(source)) {
            Console.Error.WriteLine("eggledger.subprod run: verify failed, refusing to launch");
            return 1;
        }

        var psi = new ProcessStartInfo {
            FileName = "dotnet",
            ArgumentList = { "run", "--project", "EggLedger.Web.Server", "--launch-profile", "subprod" },
            UseShellExecute = false,
        };
        using var process = Process.Start(psi);
        if (process is null) {
            Console.Error.WriteLine("eggledger.subprod run: failed to start dotnet run");
            return 1;
        }
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
