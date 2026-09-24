using System.Reflection;
using System.Text.Json;

namespace EggLedger.Domain.LedgerData;

public static class LedgerData {
    private const string ResourceName = "EggLedger.Domain.Resources.ledger-display-data-min.json";
    private static readonly Lazy<LedgerDisplayData> LazyConfig = new(LoadEmbedded);
    public static LedgerDisplayData Config => LazyConfig.Value;

    private static LedgerDisplayData LoadEmbedded() {
        var asm = typeof(LedgerData).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"embedded ledger data resource not found: {ResourceName}");

        var data = JsonSerializer.Deserialize<LedgerDisplayData>(stream)
            ?? throw new InvalidOperationException("ledger data deserialized to null");
        return data;
    }

    internal static Assembly OwningAssembly => typeof(LedgerData).Assembly;
}
