using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EggIdentity.Resilience;
using EggLedger.Domain.Crypto;
using EggLedger.Web.Platform;

namespace EggLedger.Web.Services;

public sealed class CloudSyncService(
    HttpClient http, INavigation nav, IBlobCipher cipher, IPlatformCapabilities platform, CircuitBreaker breaker) {

    public const string ApiPrefix = "api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan OpTimeout = TimeSpan.FromSeconds(12);
    private static readonly RetryOptions RetryOpts = new() {
        MaxAttempts = 2,
        BaseDelay = TimeSpan.FromMilliseconds(250),
        ShouldRetry = IsTransient,
    };

    private static bool IsTransient(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private async Task<T> ResilientAsync<T>(
        string opName, Func<CancellationToken, Task<T>> op, CancellationToken cancellationToken) {
        if (!breaker.TryEnter()) {
            throw new CloudSyncException($"{opName}: sync server unavailable right now, try again shortly");
        }
        try {
            var result = await Deadline.RunAsync(
                opName,
                ct => Retry.RunAsync(op, RetryOpts, ct: ct),
                OpTimeout,
                ct: cancellationToken).ConfigureAwait(false);
            breaker.RecordSuccess();
            return result;
        } catch (Exception ex) when (IsTransient(ex) || ex is TimeoutException) {
            breaker.RecordFailure();
            throw new CloudSyncException($"{opName}: {ex.Message}", ex);
        }
    }

    private async Task ResilientAsync(
        string opName, Func<CancellationToken, Task> op, CancellationToken cancellationToken) {
        await ResilientAsync(opName, async ct => {
            await op(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

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
        var init = await ResilientAsync("cloud-sync-auth-begin", async ct => {
            using var resp = await http.GetAsync($"{ApiPrefix}/auth/pair/begin", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"auth init: server returned {(int)resp.StatusCode}");
            }
            return await resp.Content.ReadFromJsonAsync<AuthInitResponse>(Json, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

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
        return await ResilientAsync("cloud-sync-poll", async ct => {
            var url = $"{ApiPrefix}/auth/poll?state={Uri.EscapeDataString(state)}";
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);

            switch (resp.StatusCode) {
                case HttpStatusCode.Accepted:
                case HttpStatusCode.NotFound:
                    return PollResult.StillPending;
                case HttpStatusCode.OK:
                    break;
                default:
                    throw new CloudSyncException($"auth poll: unexpected status {(int)resp.StatusCode}");
            }

            var poll = await resp.Content.ReadFromJsonAsync<PollResponse>(Json, ct).ConfigureAwait(false)
                ?? throw new CloudSyncException("auth poll: malformed success response");
            var session = new CloudSession(poll.Token, poll.Username, poll.AvatarUrl, poll.EncryptionKey);
            return PollResult.Done(session);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudSession> ConnectViaLoginAsync(CancellationToken cancellationToken = default) {
        return await ResilientAsync("cloud-sync-connect", async ct => {
            using var resp = await http.PostAsync($"{ApiPrefix}/auth/session-from-login", content: null, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.Unauthorized) {
                throw new CloudSyncException("session-from-login: not logged in");
            }
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"session-from-login: server returned {(int)resp.StatusCode}");
            }
            var poll = await resp.Content.ReadFromJsonAsync<PollResponse>(Json, ct).ConfigureAwait(false)
                ?? throw new CloudSyncException("session-from-login: malformed response");
            return new CloudSession(poll.Token, poll.Username, poll.AvatarUrl, poll.EncryptionKey);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(string token, CancellationToken cancellationToken = default) {
        await ResilientAsync("cloud-sync-disconnect", async ct => {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiPrefix}/auth/session");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
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

        await ResilientAsync($"cloud-sync-put-{name}", async ct => {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiPrefix}/blobs/{Uri.EscapeDataString(name)}");
            req.Content = JsonContent.Create(new PutBlobRequest(ciphertext), options: Json);

            using var resp = await SendAuthedAsync(session, req, $"putBlob {name}: session expired - please reconnect", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"putBlob {name}: server error {(int)resp.StatusCode}");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> GetBlobAsync<T>(CloudSession session, string name, CancellationToken cancellationToken = default) {
        var env = await ResilientAsync($"cloud-sync-get-{name}", async ct => {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiPrefix}/blobs/{Uri.EscapeDataString(name)}");

            using var resp = await SendAuthedAsync(session, req, $"getBlob {name}: session expired - please reconnect", ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) {
                throw new CloudSyncException($"getBlob {name}: not found (nothing synced yet?)");
            }
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"getBlob {name}: server error {(int)resp.StatusCode}");
            }

            return await resp.Content.ReadFromJsonAsync<GetBlobResponse>(Json, ct).ConfigureAwait(false)
                ?? throw new CloudSyncException($"getBlob {name}: malformed response");
        }, cancellationToken).ConfigureAwait(false);

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
        return await ResilientAsync<IReadOnlyList<BlobListEntry>>("cloud-sync-list", async ct => {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiPrefix}/blobs");

            using var resp = await SendAuthedAsync(session, req, "listBlobs: session expired - please reconnect", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"listBlobs: server error {(int)resp.StatusCode}");
            }
            return await resp.Content.ReadFromJsonAsync<List<BlobListEntry>>(Json, ct).ConfigureAwait(false)
                ?? [];
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBlobAsync(CloudSession session, string name, CancellationToken cancellationToken = default) {
        await ResilientAsync($"cloud-sync-delete-{name}", async ct => {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiPrefix}/blobs/{Uri.EscapeDataString(name)}");
            using var resp = await SendAuthedAsync(session, req, "deleteBlob: session expired - please reconnect", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"deleteBlob {name}: server error {(int)resp.StatusCode}");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAccountAsync(CloudSession session, CancellationToken cancellationToken = default) {
        await ResilientAsync("cloud-sync-delete-account", async ct => {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiPrefix}/user");
            using var resp = await SendAuthedAsync(session, req, "deleteAccount: session expired - please reconnect", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                throw new CloudSyncException($"deleteAccount: server error {(int)resp.StatusCode}");
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
