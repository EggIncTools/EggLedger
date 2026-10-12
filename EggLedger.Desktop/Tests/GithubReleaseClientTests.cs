using System.Net;
using EggLedger.Desktop.Update;

namespace EggLedger.Desktop.Tests;

public sealed class GithubReleaseClientTests {
    [Fact]
    public async Task GetLatestTag_ParsesTagAndBodyFromExpectedUrl() {
        var stub = new StubHttpMessageHandler(_ =>
            StubResponses.Json(HttpStatusCode.OK, """{"tag_name":"2.2.0","body":"notes here"}"""));
        var client = new GithubReleaseClient(new HttpClient(stub));

        var rel = await client.GetLatestTagAsync();

        Assert.NotNull(rel);
        Assert.Equal("2.2.0", rel.Value.Tag);
        Assert.Equal("notes here", rel.Value.Body);
        Assert.Contains("https://api.github.com/repos/EggIncTools/EggLedger/releases/latest", stub.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task GetLatestTag_NullOnEmptyTag() {
        var stub = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, """{"tag_name":"","body":""}"""));
        var client = new GithubReleaseClient(new HttpClient(stub));
        Assert.Null(await client.GetLatestTagAsync());
    }

    [Fact]
    public async Task GetLatestTagIncludingPreReleases_PicksHighestNonDraft() {
        var json = """
        [
          {"tag_name":"2.2.0-rc.1","body":"rc","draft":false},
          {"tag_name":"2.3.0","body":"draft notes","draft":true},
          {"tag_name":"2.2.0","body":"stable","draft":false}
        ]
        """;
        var stub = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, json));
        var client = new GithubReleaseClient(new HttpClient(stub));

        var rel = await client.GetLatestTagIncludingPreReleasesAsync();

        Assert.NotNull(rel);

        Assert.Equal("2.2.0", rel.Value.Tag);
        Assert.Contains("releases?per_page=10", stub.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task GetUpdateAssetUrl_ReturnsMatchingPlatformAsset() {
        var want = GithubReleaseClient.ExpectedAssetName();
        var json = $$"""
        {"assets":[
          {"name":"other.zip","browser_download_url":"https://x/other.zip"},
          {"name":"{{want}}","browser_download_url":"https://x/{{want}}"}
        ]}
        """;
        var stub = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, json));
        var client = new GithubReleaseClient(new HttpClient(stub));

        var url = await client.GetUpdateAssetUrlAsync("2.2.0");

        Assert.Equal($"https://x/{want}", url);
        Assert.Contains("releases/tags/2.2.0", stub.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task GetUpdateAssetUrl_NullWhenNoMatch() {
        var json = """{"assets":[{"name":"nope.zip","browser_download_url":"https://x/nope.zip"}]}""";
        var stub = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, json));
        var client = new GithubReleaseClient(new HttpClient(stub));
        Assert.Null(await client.GetUpdateAssetUrlAsync("2.2.0"));
    }

    [Fact]
    public async Task Download_WritesBytesAndReportsProgress() {
        var payload = new byte[200 * 1024];
        for (var i = 0; i < payload.Length; i++) {
            payload[i] = (byte)(i % 251);
        }
        var stub = new StubHttpMessageHandler(_ => StubResponses.Bytes(HttpStatusCode.OK, payload));
        var client = new GithubReleaseClient(new HttpClient(stub));

        using var temp = new TempDir("egg-dl");
        var dest = temp.File("dl.bin");
        long lastReported = 0;
        await client.DownloadAsync("https://x/EggLedger.exe", dest, (d, _) => lastReported = d);

        Assert.True(File.Exists(dest));
        var written = await File.ReadAllBytesAsync(dest);
        Assert.Equal(payload.Length, written.Length);
        Assert.Equal(payload, written);
        Assert.Equal(payload.Length, lastReported);
        Assert.Contains("https://x/EggLedger.exe", stub.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task Download_ThrowsOnTruncatedContent() {

        var stub = new StubHttpMessageHandler(_ => {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentLength = 999;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content };
        });
        var client = new GithubReleaseClient(new HttpClient(stub));
        using var temp = new TempDir("egg-trunc");
        await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync("https://x/a", temp.File("dl.bin"), null));
    }
}
