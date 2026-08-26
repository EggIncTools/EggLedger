using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EggLedger.Domain.Crypto;
using EggLedger.Web.Platform;

namespace EggLedger.Web.Services;

public sealed class CloudSyncService(
    HttpClient http, INavigation nav, IBlobCipher cipher, IPlatformCapabilities platform) {

    public const string ApiPrefix = "api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> CheckReachableAsync(CancellationToken cancellationToken = default) {
        try {
            using var resp = await http.GetAsync($"{ApiPrefix}/verify", cancellationToken).ConfigureAwait(false);
            return resp.StatusCode == HttpStatusCode.OK;
        } catch (HttpRequestException) {
            return false;
        } catch (TaskCanceledException) {
            return false;
        }
    }

    public async Task<string> BeginAuthAsync(CancellationToken cancellationToken = default) {
        AuthInitResponse? init;
        using (var resp = await http.GetAsync($"{ApiPrefix}/auth/pair/begin", cancellationToken).ConfigureAwait(false)) {
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"auth init: server returned {(int)resp.StatusCode}");
            }
            init = await resp.Content.ReadFromJsonAsync<AuthInitResponse>(Json, cancellationToken).ConfigureAwait(false);
        }
        if (init is null || string.IsNullOrEmpty(init.Url) || string.IsNullOrEmpty(init.State)) {
            throw new CloudSyncException("auth init: malformed response");
        }

        if (platform.IsDesktop) {
            await platform.OpenUrlAsync(init.Url);
        } else {
            nav.NavigateTo(init.Url);
        }
        return init.State;
    }

    public async Task<PollResult> PollOnceAsync(string state, CancellationToken cancellationToken = default) {
        var url = $"{ApiPrefix}/auth/poll?state={Uri.EscapeDataString(state)}";
        using var resp = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);

        switch (resp.StatusCode) {
            case HttpStatusCode.Accepted:
            case HttpStatusCode.NotFound:
                return PollResult.StillPending;
            case HttpStatusCode.OK:
                break;
            default:
                throw new CloudSyncException($"auth poll: unexpected status {(int)resp.StatusCode}");
        }

        var poll = await resp.Content.ReadFromJsonAsync<PollResponse>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new CloudSyncException("auth poll: malformed success response");
        var session = new CloudSession(poll.Token, poll.Username, poll.AvatarUrl, poll.EncryptionKey);
        return PollResult.Done(session);
    }

    public async Task<CloudSession> ConnectViaLoginAsync(CancellationToken cancellationToken = default) {
        using var resp = await http.PostAsync($"{ApiPrefix}/auth/session-from-login", content: null, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) {
            throw new CloudSyncException("session-from-login: not logged in");
        }
        if (!resp.IsSuccessStatusCode) {
            throw new CloudSyncException($"session-from-login: server returned {(int)resp.StatusCode}");
        }
        var poll = await resp.Content.ReadFromJsonAsync<PollResponse>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new CloudSyncException("session-from-login: malformed response");
        return new CloudSession(poll.Token, poll.Username, poll.AvatarUrl, poll.EncryptionKey);
    }

    public async Task DisconnectAsync(string token, CancellationToken cancellationToken = default) {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiPrefix}/auth/session");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAuthedAsync(
        CloudSession session, HttpRequestMessage request, string expiredMessage, CancellationToken cancellationToken) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var resp = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) {
            resp.Dispose();
            throw new CloudSyncException(expiredMessage);
        }
        return resp;
    }

    public async Task PutBlobAsync<T>(CloudSession session, string name, T payload, CancellationToken cancellationToken = default) {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var ciphertext = await cipher.EncryptAsync(session.EncryptionKey, plaintext, cancellationToken).ConfigureAwait(false);

        using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiPrefix}/blobs/{Uri.EscapeDataString(name)}");
        req.Content = JsonContent.Create(new PutBlobRequest(ciphertext), options: Json);

        using var resp = await SendAuthedAsync(session, req, $"putBlob {name}: session expired - please reconnect", cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) {
            throw new CloudSyncException($"putBlob {name}: server error {(int)resp.StatusCode}");
        }
    }

    public async Task<T> GetBlobAsync<T>(CloudSession session, string name, CancellationToken cancellationToken = default) {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiPrefix}/blobs/{Uri.EscapeDataString(name)}");

        using var resp = await SendAuthedAsync(session, req, $"getBlob {name}: session expired - please reconnect", cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) {
            throw new CloudSyncException($"getBlob {name}: not found (nothing synced yet?)");
        }
        if (!resp.IsSuccessStatusCode) {
            throw new CloudSyncException($"getBlob {name}: server error {(int)resp.StatusCode}");
        }

        var env = await resp.Content.ReadFromJsonAsync<GetBlobResponse>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new CloudSyncException($"getBlob {name}: malformed response");
        byte[] plaintext;
        try {
            plaintext = await cipher.DecryptAsync(session.EncryptionKey, env.Ciphertext, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not CloudSyncException) {
            throw new CloudSyncException($"getBlob {name}: decrypt failed: {ex.Message}", ex);
        }

        var value = JsonSerializer.Deserialize<T>(plaintext, Json) ?? throw new CloudSyncException($"getBlob {name}: decoded payload was null");
        return value;
    }

    public async Task<IReadOnlyList<BlobListEntry>> ListBlobsAsync(CloudSession session, CancellationToken cancellationToken = default) {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiPrefix}/blobs");

        using var resp = await SendAuthedAsync(session, req, "listBlobs: session expired - please reconnect", cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) {
            throw new CloudSyncException($"listBlobs: server error {(int)resp.StatusCode}");
        }
        return await resp.Content.ReadFromJsonAsync<List<BlobListEntry>>(Json, cancellationToken).ConfigureAwait(false)
            ?? [];
    }

    public async Task DeleteBlobAsync(CloudSession session, string name, CancellationToken cancellationToken = default) {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiPrefix}/blobs/{Uri.EscapeDataString(name)}");
        using var resp = await SendAuthedAsync(session, req, "deleteBlob: session expired - please reconnect", cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) {
            throw new CloudSyncException($"deleteBlob {name}: server error {(int)resp.StatusCode}");
        }
    }

    public async Task DeleteAccountAsync(CloudSession session, CancellationToken cancellationToken = default) {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiPrefix}/user");
        using var resp = await SendAuthedAsync(session, req, "deleteAccount: session expired - please reconnect", cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) {
            throw new CloudSyncException($"deleteAccount: server error {(int)resp.StatusCode}");
        }
    }
}
