using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggLedger.Desktop.Storage;

public static class StoragePaths {
    private const string AppDirName = "EggLedger";
    private const string BootstrapFileName = "bootstrap.json";

    public static string DefaultRootDir() {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = string.IsNullOrEmpty(local) ? AppContext.BaseDirectory : local;
        return Path.Combine(baseDir, AppDirName);
    }

    public static string BootstrapPath() {
        var configDir = UserConfigDir();
        return configDir == "" ? "" : Path.Combine(configDir, AppDirName, BootstrapFileName);
    }

    public static string ResolveInternalDir(string rootDir) {
        var cfg = ReadBootstrapConfig();
        if (cfg.DataRootDir != "") {
            return Path.Combine(cfg.DataRootDir, "internal");
        }
        return cfg.InternalDir != "" ? cfg.InternalDir : Path.Combine(rootDir, "internal");
    }

    public static string ResolveExportsDir(string rootDir) {
        var cfg = ReadBootstrapConfig();
        return cfg.DataRootDir != ""
            ? Path.Combine(cfg.DataRootDir, "exports")
            : Path.Combine(rootDir, "exports");
    }

    public static string ResolveLogsDir(string rootDir) {
        var cfg = ReadBootstrapConfig();
        return cfg.DataRootDir != ""
            ? Path.Combine(cfg.DataRootDir, "logs")
            : Path.Combine(rootDir, "logs");
    }

    public static string ResolveDataRootDir(string rootDir) {
        var cfg = ReadBootstrapConfig();
        return cfg.DataRootDir != "" ? cfg.DataRootDir : rootDir;
    }

    public static void WriteBootstrapConfig(string dataRootDir) {
        var path = BootstrapPath();
        if (path == "") {
            throw new FileNotFoundException("user config dir is unavailable");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new BootstrapConfig { DataRootDir = dataRootDir }, JsonOpts);
        File.WriteAllText(path, json);
    }

    public static BootstrapConfig ReadBootstrapConfig() {
        var path = BootstrapPath();
        if (path == "" || !File.Exists(path)) {
            return new BootstrapConfig();
        }
        try {
            var data = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BootstrapConfig>(data, JsonOpts) ?? new BootstrapConfig();
        } catch (Exception ex) when (ex is JsonException or IOException) {
            return new BootstrapConfig();
        }
    }

    public static void CopyDir(string src, string dst) {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(src, file);
            CopyFile(file, Path.Combine(dst, rel));
        }
    }

    public static void CopyFile(string src, string dst) {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
    }

    private static string UserConfigDir() {

        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(dir) ? "" : dir;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public sealed class BootstrapConfig {
        [JsonPropertyName("data_root_dir")]
        public string DataRootDir { get; set; } = "";

        [JsonPropertyName("internal_dir")]
        public string InternalDir { get; set; } = "";
    }
}
