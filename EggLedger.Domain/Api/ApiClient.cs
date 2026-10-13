using System.IO.Compression;
using EggLedger.Domain.Ei;
using EggSharp.Api;
using EggSharp.Core;
using Ei;
using ProtoBuf;

namespace EggLedger.Domain.Api;

public sealed partial class ApiClient(IEggIncApi? api = null, string apiPrefix = ApiClient.DefaultApiPrefix) {
    public const string DefaultAppVersion = "1.35.7";
    public const string DefaultAppBuild = "111343";
    public const uint DefaultClientVersion = 72;
    public const Platform DefaultPlatform = Platform.Ios;
    public const string DefaultApiPrefix = "https://ctx-dot-auxbrainhome.appspot.com";
    public static readonly VersionTriple DefaultVersions = new(DefaultClientVersion, DefaultAppVersion, DefaultAppBuild);
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    public string ApiPrefix { get; } = apiPrefix;
    public string AppVersion => DefaultAppVersion;
    public string AppBuild => DefaultAppBuild;
    public uint ClientVersion => DefaultClientVersion;
    public Platform Platform => DefaultPlatform;

    private IEggIncApi Api => api ?? throw new InvalidOperationException("ApiClient was built without an IEggIncApi; it can only decode.");

    public BasicRequestInfo NewBasicRequestInfo(string userId) => new() {
        EiUserId = userId,
        ClientVersion = ClientVersion,
        Version = AppVersion,
        Build = AppBuild,
        Platform = EnumNames.ProtoName(Platform),
    };

    private async Task<byte[]> SendAsync(
        string endpoint,
        Func<IEggIncApi, Task<ApiOutcome<byte[]>>> call,
        CancellationToken cancellationToken) {
        string apiUrl = ApiPrefix + endpoint;
        ApiOutcome<byte[]> outcome;
        try {
            outcome = await call(Api).ConfigureAwait(false);
        } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new ApiRequestException($"POST {apiUrl}: timeout after {RequestTimeout}");
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new ApiRequestException($"POST {apiUrl}: interrupted");
        } catch (HttpRequestException ex) {
            throw new ApiRequestException($"POST {apiUrl}: {ex.Message}", ex);
        } catch (FormatException ex) {
            throw new ApiRequestException($"POST {apiUrl}: base64 decode error: {ex.Message}", ex);
        }

        if (outcome.StatusCode is { } status) {
            throw new ApiRequestException($"POST {apiUrl}: HTTP {status}: {outcome.ResponseBody}");
        }
        if (!outcome.Success || outcome.Response is null) {
            throw new ApiRequestException($"POST {apiUrl}: {outcome.Error}");
        }
        return outcome.Response;
    }

    public static TMsg DecodeApiResponse<TMsg>(string apiUrl, byte[] payload, bool authenticated) {
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
        for (Exception? e = ex; e is not null; e = e.InnerException) parts.Add($"{e.GetType().Name}: {e.Message}");
        return string.Join(" -> ", parts);
    }

    private static ApiRequestException InterpretUnmarshalError(string message, Exception inner) =>
        inner.Message.Contains("invalid UTF-8", StringComparison.OrdinalIgnoreCase) ||
        inner.Message.Contains("invalid UTF8", StringComparison.OrdinalIgnoreCase)
            ? new ApiRequestException(
                "API returned corrupted data (invalid UTF-8 in one or more string fields); " +
                "this is a known issue affecting some players, and it can only be resolved when " +
                "Auxbrain fixes their server bug", inner)
            : new ApiRequestException(message, inner);
}

public sealed class ApiRequestException : Exception {
    public ApiRequestException(string message) : base(message) { }

    public ApiRequestException(string message, Exception inner) : base(message, inner) { }
}
