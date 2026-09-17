using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace EggLedger.Web.Data;

public static class JsonRowCodec {
    public readonly record struct Provider(bool BoolAsInteger, Func<int, string> KeyPlaceholder);
    public static readonly Provider Sqlite = new(BoolAsInteger: true, _ => "?");
    public static readonly Provider Postgres = new(BoolAsInteger: false, i => "@k" + i.ToString(CultureInfo.InvariantCulture));

    public static T Materialize<T>(
        DbDataReader reader,
        string table,
        Func<string, bool> isBool,
        Func<string, bool> isBlob,
        JsonSerializerOptions jsonOpts,
        Func<string, bool>? skipColumn = null) {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            for (var i = 0; i < reader.FieldCount; i++) {
                var name = reader.GetName(i);
                if (skipColumn is not null && skipColumn(name)) {
                    continue;
                }
                writer.WritePropertyName(name);
                WriteColumn(writer, reader, i, isBool(name), isBlob(name));
            }
            writer.WriteEndObject();
        }
        var json = buffer.ToArray();
        return JsonSerializer.Deserialize<T>(json, jsonOpts)
            ?? throw new InvalidOperationException($"failed to materialize row for {table}");
    }

    public static void WriteColumn(Utf8JsonWriter writer, DbDataReader reader, int i, bool isBool, bool isBlob) {
        if (reader.IsDBNull(i)) {
            writer.WriteNullValue();
            return;
        }
        if (isBlob) {
            writer.WriteBase64StringValue((byte[])reader.GetValue(i));
            return;
        }
        if (isBool) {
            writer.WriteBooleanValue(reader.GetInt64(i) != 0);
            return;
        }
        switch (reader.GetFieldType(i)) {
            case var t when t == typeof(bool):
                writer.WriteBooleanValue(reader.GetBoolean(i));
                break;
            case var t when t == typeof(DateTime) || t == typeof(DateTimeOffset):
                WriteEpochValue(writer, reader, i);
                break;
            case var t when t == typeof(long):
                writer.WriteNumberValue(reader.GetInt64(i));
                break;
            case var t when t == typeof(int):
                writer.WriteNumberValue(reader.GetInt32(i));
                break;
            case var t when t == typeof(double):
                writer.WriteNumberValue(reader.GetDouble(i));
                break;
            case var t when t == typeof(byte[]):
                writer.WriteBase64StringValue((byte[])reader.GetValue(i));
                break;
            default:
                writer.WriteStringValue(reader.GetString(i));
                break;
        }
    }

    private static void WriteEpochValue(Utf8JsonWriter writer, DbDataReader reader, int i) {
        var millis = reader.GetFieldValue<DateTimeOffset>(i).ToUnixTimeMilliseconds();
        var (seconds, remainder) = Math.DivRem(millis, 1000L);
        if (remainder == 0) {
            writer.WriteNumberValue(seconds);
            return;
        }
        writer.WriteNumberValue(millis / 1000d);
    }

    public static object JsonToDbValue(JsonElement el, bool isBlob, Provider provider) =>
        JsonToDbValue(el, isBlob, isEpoch: false, provider);

    public static object JsonToDbValue(JsonElement el, bool isBlob, bool isEpoch, Provider provider) => el.ValueKind switch {
        JsonValueKind.Null => DBNull.Value,
        JsonValueKind.True => provider.BoolAsInteger ? 1L : true,
        JsonValueKind.False => provider.BoolAsInteger ? 0L : false,
        JsonValueKind.Number when isEpoch && !provider.BoolAsInteger => EpochToDb(el),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.String => DecodeString(el, isBlob),
        _ => el.GetRawText(),
    };

    private static DateTimeOffset EpochToDb(JsonElement el) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(el.GetDouble() * 1000d));

    public static object DecodeString(JsonElement el, bool isBlob) {
        var s = el.GetString() ?? "";
        return isBlob ? Convert.FromBase64String(s) : s;
    }

    public static (string where, object[] args) KeyPredicate(
        string table, string[] keyColumns, object key, Func<string, string> ident, Provider provider) {
        if (keyColumns.Length == 1) {
            return ($"{ident(keyColumns[0])} = {provider.KeyPlaceholder(0)}", [key]);
        }
        if (key is object[] parts && parts.Length == keyColumns.Length) {
            var where = string.Join(" AND ",
                keyColumns.Select((c, i) => $"{ident(c)} = {provider.KeyPlaceholder(i)}"));
            return (where, parts);
        }
        throw new ArgumentException($"composite key expected for store {table}", nameof(key));
    }
}
