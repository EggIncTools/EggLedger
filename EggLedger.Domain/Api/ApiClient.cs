using System.IO.Compression;
using EggLedger.Domain.Ei;
using Ei;
using ProtoBuf;

namespace EggLedger.Domain.Api;

public sealed partial class ApiClient(
    HttpClient? httpClient = null,
    string apiPrefix = ApiClient.DefaultApiPrefix,
    string appVersion = ApiClient.DefaultAppVersion,
    string appBuild = ApiClient.DefaultAppBuild,
    uint clientVersion = ApiClient.DefaultClientVersion,
    Platform platform = ApiClient.DefaultPlatform) {

    public const string DefaultAppVersion = "1.35.7";
    public const string DefaultAppBuild = "111343";
    public const uint DefaultClientVersion = 72;
    public const Platform DefaultPlatform = Platform.Ios;
    public const string DefaultApiPrefix = "https://ctx-dot-auxbrainhome.appspot.com";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient _client = httpClient ?? new HttpClient { Timeout = RequestTimeout };
    public string ApiPrefix { get; } = apiPrefix;
    public string AppVersion { get; } = appVersion;
    public string AppBuild { get; } = appBuild;
    public uint ClientVersion { get; } = clientVersion;
    public Platform Platform { get; } = platform;

    public BasicRequestInfo NewBasicRequestInfo(string userId) => new() {
        EiUserId = userId,
        ClientVersion = ClientVersion,
        Version = AppVersion,
        Build = AppBuild,
        Platform = EnumNames.ProtoName(Platform),
    };

    public async Task<byte[]> RequestRawPayloadAsync<TReq>(
        string endpoint,
        TReq reqMsg,
        CancellationToken cancellationToken = default) {
        string apiUrl = ApiPrefix + endpoint;

        byte[] reqBin;
        using (var buffer = new MemoryStream()) {
            Serializer.Serialize(buffer, reqMsg);
            reqBin = buffer.ToArray();
        }

        string reqDataEncoded = Convert.ToBase64String(reqBin);
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("data", reqDataEncoded),
        });

        HttpResponseMessage resp;
        try {
            resp = await _client.PostAsync(apiUrl, form, cancellationToken).ConfigureAwait(false);
        } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new ApiRequestException($"POST {apiUrl}: timeout after {RequestTimeout}");
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new ApiRequestException($"POST {apiUrl}: interrupted");
        } catch (HttpRequestException ex) {
            throw new ApiRequestException($"POST {apiUrl}: {ex.Message}", ex);
        }

        using (resp) {
            string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) {
                throw new ApiRequestException(
                    $"POST {apiUrl}: HTTP {(int)resp.StatusCode}: {body}");
            }

            try {
                return Convert.FromBase64String(body);
            } catch (FormatException ex) {
                throw new ApiRequestException(
                    $"POST {apiUrl}: {body}: base64 decode error: {ex.Message}", ex);
            }
        }
    }

    public TMsg DecodeApiResponse<TMsg>(string apiUrl, byte[] payload, bool authenticated) {
        if (!authenticated) {
            try {
                return Deserialize<TMsg>(payload);
            } catch (Exception ex) {
                throw InterpretUnmarshalError(
                    $"unmarshaling {apiUrl} response: {ChainText(ex)}", ex);
            }
        }

        AuthenticatedMessage authMsg;
        try {
            authMsg = Deserialize<AuthenticatedMessage>(payload);
        } catch (Exception ex) {
            throw InterpretUnmarshalError(
                $"unmarshaling {apiUrl} response as AuthenticatedMessage: {ChainText(ex)}", ex);
        }

        byte[] msgBytes = authMsg.Message ?? [];
        if (authMsg.Compressed) {
            try {
                msgBytes = ZlibInflate(msgBytes);
            } catch (Exception ex) {
                throw new ApiRequestException(
                    $"decompressing AuthenticatedMessage payload in {apiUrl}: {ex.Message}", ex);
            }
        }

        try {
            return Deserialize<TMsg>(msgBytes);
        } catch (Exception ex) {
            throw InterpretUnmarshalError(
                $"unmarshaling AuthenticatedMessage payload in {apiUrl} response: {ChainText(ex)}", ex);
        }
    }

    private static TMsg Deserialize<TMsg>(byte[] data) {
        using var ms = new MemoryStream(data, writable: false);
        return Serializer.Deserialize<TMsg>(ms);
    }

    private static byte[] ZlibInflate(byte[] data) {
        using var input = new MemoryStream(data, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static string ChainText(Exception ex) {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) {
            parts.Add($"{e.GetType().Name}: {e.Message}");
        }
        return string.Join(" -> ", parts);
    }

    private static ApiRequestException InterpretUnmarshalError(string message, Exception inner) {
        if (inner.Message.Contains("invalid UTF-8", StringComparison.OrdinalIgnoreCase) ||
            inner.Message.Contains("invalid UTF8", StringComparison.OrdinalIgnoreCase)) {
            return new ApiRequestException(
                "API returned corrupted data (invalid UTF-8 in one or more string fields); " +
                "this is a known issue affecting some players, and it can only be resolved when " +
                "Auxbrain fixes their server bug", inner);
        }
        return new ApiRequestException(message, inner);
    }
}

public sealed class ApiRequestException : Exception {
    public ApiRequestException(string message) : base(message) { }

    public ApiRequestException(string message, Exception inner) : base(message, inner) { }
}
