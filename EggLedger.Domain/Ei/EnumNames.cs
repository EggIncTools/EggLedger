using System.Collections.Concurrent;
using System.Reflection;
using ProtoBuf;

namespace EggLedger.Domain.Ei;

internal static class EnumNames {
    private static readonly ConcurrentDictionary<Type, Dictionary<long, string>> _valueToName = new();
    private static readonly ConcurrentDictionary<Type, Dictionary<string, long>> _nameToValue = new();

    public static string ProtoName<TEnum>(TEnum value) where TEnum : struct, Enum {
        var map = ValueMap(typeof(TEnum));
        long key = Convert.ToInt64(value);
        return map.TryGetValue(key, out var name) ? name : key.ToString();
    }

    public static bool TryValue<TEnum>(string protoName, out TEnum value) where TEnum : struct, Enum {
        var map = NameMap(typeof(TEnum));
        if (map.TryGetValue(protoName, out var raw)) {
            value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
            return true;
        }
        value = default;
        return false;
    }

    private static Dictionary<long, string> ValueMap(Type enumType) =>
        _valueToName.GetOrAdd(enumType, Build);

    private static Dictionary<string, long> NameMap(Type enumType) =>
        _nameToValue.GetOrAdd(enumType, t => {
            var result = new Dictionary<string, long>();
            foreach (var kv in Build(t)) {
                result[kv.Value] = kv.Key;
            }
            return result;
        });

    private static Dictionary<long, string> Build(Type enumType) {
        var result = new Dictionary<long, string>();
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static)) {
            long key = Convert.ToInt64(field.GetRawConstantValue());
            var attr = field.GetCustomAttribute<ProtoEnumAttribute>();
            string name = attr?.Name ?? field.Name;
            result[key] = name;
        }
        return result;
    }
}
