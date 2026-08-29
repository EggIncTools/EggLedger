using System.Runtime.InteropServices;
using System.Text.Json;

namespace EggLedger.Desktop.Update;

public sealed class GithubReleaseClient(HttpClient httpClient) {
    public const string GithubRepo = "DavidArthurCole/EggLedger";

    private const int DownloadChunkBytes = 64 * 1024;

    private readonly HttpClient _httpClient = httpClient;

    public readonly record struct Release(string Tag, string Body);

    public async Task<Release?> GetLatestTagAsync(CancellationToken cancel = default) {
        var url = $"https://api.github.com/repos/{GithubRepo}/releases/latest";
        var json = await GetStringAsync(url, cancel).ConfigureAwait(false);
        if (json is null) {
            return null;
        }
        return TryParseJson(json, root => {
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag)) {
                return (Release?)null;
            }
            var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new Release(tag, body);
        });
    }

    public async Task<Release?> GetLatestTagIncludingPreReleasesAsync(CancellationToken cancel = default) {
        var url = $"https://api.github.com/repos/{GithubRepo}/releases?per_page=10";
        var json = await GetStringAsync(url, cancel).ConfigureAwait(false);
        if (json is null) {
            return null;
        }

        return TryParseJson(json, root => {
            if (root.ValueKind != JsonValueKind.Array) {
                return null;
            }
            EggLedger.Domain.Util.SemVersion? highest = null;
            Release? best = null;
            foreach (var rel in root.EnumerateArray()) {
                if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) {
                    continue;
                }
                var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(tag) || !Domain.Util.SemVersion.TryParse(tag, out var v) || v is null) {
                    continue;
                }
                if (highest is null || v.GreaterThan(highest)) {
                    highest = v;
                    var body = rel.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                    best = new Release(tag, body);
                }
            }
            return best;
        });
    }

    public static string ExpectedAssetName() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return "EggLedger.exe";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return "EggLedger-linux.tar.gz";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? "EggLedger-mac-arm64.zip"
                : "EggLedger-mac.zip";
        }
        return "EggLedger";
    }

    public async Task<string?> GetUpdateAssetUrlAsync(string tag, CancellationToken cancel = default) {
        var url = $"https://api.github.com/repos/{GithubRepo}/releases/tags/{tag}";
        var json = await GetStringAsync(url, cancel).ConfigureAwait(false);
        if (json is null) {
            return null;
        }

        return TryParseJson(json, root => {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) {
                return null;
            }
            var want = ExpectedAssetName();
            foreach (var asset in assets.EnumerateArray()) {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name == want) {
                    var dl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    return string.IsNullOrEmpty(dl) ? null : dl;
                }
            }
            return null;
        });
    }

    public async Task<string?> GetExpectedSha256Async(string tag, CancellationToken cancel = default) {
        var listUrl = $"https://api.github.com/repos/{GithubRepo}/releases/tags/{tag}";
        var json = await GetStringAsync(listUrl, cancel).ConfigureAwait(false);
        if (json is null) {
            return null;
        }
        var sumsUrl = TryParseJson(json, root => {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) {
                return null;
            }
            foreach (var asset in assets.EnumerateArray()) {
                if ((asset.TryGetProperty("name", out var n) ? n.GetString() : null) == "SHA256SUMS") {
                    return asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                }
            }
            return null;
        });
        if (string.IsNullOrEmpty(sumsUrl)) {
            return null;
        }

        var sums = await GetStringAsync(sumsUrl, cancel).ConfigureAwait(false);
        if (sums is null) {
            return null;
        }
        var want = ExpectedAssetName();
        foreach (var line in sums.Split('\n')) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) {
                continue;
            }
            var parts = trimmed.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Trim().TrimStart('*') == want) {
                return parts[0].Trim().ToLowerInvariant();
            }
        }
        return null;
    }

    public async Task DownloadAsync(
        string assetUrl,
        string destPath,
        Action<long, long>? progress,
        CancellationToken cancel = default) {
        using var resp = await _httpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancel)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[DownloadChunkBytes];
        long downloaded = 0;
        long lastReport = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0) {
            await dst.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            downloaded += read;
            if (downloaded - lastReport >= DownloadChunkBytes) {
                progress?.Invoke(downloaded, total);
                lastReport = downloaded;
            }
        }
        progress?.Invoke(downloaded, total);
        await dst.FlushAsync(cancel).ConfigureAwait(false);

        if (total > 0 && downloaded != total) {
            throw new IOException($"incomplete download: received {downloaded} of {total} bytes");
        }
    }



    private static T? TryParseJson<T>(string json, Func<JsonElement, T?> extract) {
        try {
            using var doc = JsonDocument.Parse(json);
            return extract(doc.RootElement);
        } catch (JsonException) {
            return default;
        }
    }

    private async Task<string?> GetStringAsync(string url, CancellationToken cancel) {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0) {
                req.Headers.UserAgent.ParseAdd("EggLedger");
            }
            using var resp = await _httpClient.SendAsync(req, cancel).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                return null;
            }
            return await resp.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            return null;
        }
    }
}
