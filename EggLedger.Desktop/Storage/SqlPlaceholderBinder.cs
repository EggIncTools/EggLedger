using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EggLedger.Desktop.Storage;

public static class SqlPlaceholderBinder {
    public static string Rewrite(string sql, IReadOnlyList<object?> args, SqliteCommand cmd, string paramPrefix) {
        var result = sql;
        var idx = 0;
        while (true) {
            var pos = result.IndexOf('?', StringComparison.Ordinal);
            if (pos < 0) {
                break;
            }
            var name = "@" + paramPrefix + idx.ToString(CultureInfo.InvariantCulture);
            result = result.Remove(pos, 1).Insert(pos, name);
            cmd.Parameters.AddWithValue(name, idx < args.Count ? args[idx] ?? DBNull.Value : DBNull.Value);
            idx++;
        }
        return idx == args.Count
            ? result
            : throw new InvalidOperationException(
                $"placeholder/arg mismatch: {idx} '?' placeholders but {args.Count} args");
    }
}
