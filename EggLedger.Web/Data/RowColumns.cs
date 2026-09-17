using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace EggLedger.Web.Data;

public static class RowColumns {
    private static readonly ConcurrentDictionary<Type, string[]> _cache = new();

    public static IReadOnlyList<string> Of<T>() =>
        _cache.GetOrAdd(typeof(T), static t =>
            [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
                .Where(n => n is not null)
                .Cast<string>()]);
}
