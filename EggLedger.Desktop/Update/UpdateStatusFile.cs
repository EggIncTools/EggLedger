using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EggLedger.Desktop.Update;

public sealed class UpdateStatus {
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Message { get; init; }

    [JsonPropertyName("toVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ToVersion { get; init; }

    [JsonPropertyName("newBinary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? NewBinary { get; init; }
}

public static class UpdateStatusFile {
    public const string FileName = ".egg-update-status.json";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    private static string PathFor(string dir) => Path.Combine(dir, FileName);

    public static void Write(string dir, UpdateStatus status) {
        var data = JsonSerializer.Serialize(status, JsonOptions);
        File.WriteAllText(PathFor(dir), data);
    }

    public static UpdateStatus? ReadAndClear(string dir, ILogger? logger = null) {
        var path = PathFor(dir);
        string data;
        try {
            data = File.ReadAllText(path);
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            return null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger?.LogDebug(ex, "update: status file read failed");
            return null;
        }

        try {
            File.Delete(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger?.LogDebug(ex, "update: status file delete failed");
        }

        try {
            return JsonSerializer.Deserialize<UpdateStatus>(data, JsonOptions);
        } catch (JsonException ex) {
            logger?.LogDebug(ex, "update: status file is not valid JSON");
            return null;
        }
    }
}
