using System.IO.Compression;
using System.Net;
using EggLedger.Domain.Api;
using Ei;
using ProtoBuf;

namespace EggLedger.Domain.Tests.Api;

public class ApiClientTests {
    private static byte[] Serialize<T>(T msg) {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] data) {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    [Fact]
    public void DecodeApiResponse_FirstContact_RoundTrips() {

        var backup = new Backup {
            EiUserId = "EI1234567890123456",
            artifacts = new Backup.Artifacts { LastFueledShip = MissionInfo.Spaceship.Henerprise },
            game = new Backup.Game { SoulEggsD = 1234.5, EggsOfProphecy = 7 },
        };
        var fc = new EggIncFirstContactResponse {
            EiUserId = "EI1234567890123456",
            ErrorCode = 0,
            Backup = backup,
        };

        byte[] payload = Serialize(fc);

        var got = ApiClient.DecodeApiResponse<EggIncFirstContactResponse>(
            "https://example/test", payload, authenticated: false);

        Assert.Equal("EI1234567890123456", got.EiUserId);
        Assert.NotNull(got.Backup);
        Assert.Equal(MissionInfo.Spaceship.Henerprise, got.Backup!.artifacts!.LastFueledShip);
        Assert.Equal(1234.5, got.Backup.game!.SoulEggsD);
        Assert.Equal(7u, got.Backup.game.EggsOfProphecy);
    }

    [Fact]
    public void DecodeApiResponse_CompleteMission_RoundTrips() {
        var info = new MissionInfo {
            Identifier = "mission-abc",
            Ship = MissionInfo.Spaceship.Henerprise,
            duration_type = MissionInfo.DurationType.Epic,
        };
        var resp = new CompleteMissionResponse {
            Success = true,
            EiUserId = "EI999",
            Info = info,
        };

        byte[] payload = Serialize(resp);

        var got = ApiClient.DecodeApiResponse<CompleteMissionResponse>(
            "https://example/test", payload, authenticated: false);

        Assert.True(got.Success);
        Assert.Equal("EI999", got.EiUserId);
        Assert.Equal("mission-abc", got.Info!.Identifier);
        Assert.Equal(MissionInfo.Spaceship.Henerprise, got.Info.Ship);
        Assert.Equal(MissionInfo.DurationType.Epic, got.Info.duration_type);
    }

    [Fact]
    public void DecodeApiResponse_Authenticated_Compressed() {
        var resp = new CompleteMissionResponse {
            Success = true,
            Info = new MissionInfo { Identifier = "zipped" },
        };
        byte[] inner = Serialize(resp);
        var auth = new AuthenticatedMessage {
            Message = ZlibCompress(inner),
            Compressed = true,
        };
        byte[] payload = Serialize(auth);

        var got = ApiClient.DecodeApiResponse<CompleteMissionResponse>(
            "https://example/test", payload, authenticated: true);

        Assert.True(got.Success);
        Assert.Equal("zipped", got.Info!.Identifier);
    }

    [Fact]
    public void DecodeApiResponse_Authenticated_Uncompressed() {
        var resp = new CompleteMissionResponse {
            Success = true,
            Info = new MissionInfo { Identifier = "plain" },
        };
        var auth = new AuthenticatedMessage {
            Message = Serialize(resp),
            Compressed = false,
        };
        byte[] payload = Serialize(auth);

        var got = ApiClient.DecodeApiResponse<CompleteMissionResponse>(
            "https://example/test", payload, authenticated: true);

        Assert.True(got.Success);
        Assert.Equal("plain", got.Info!.Identifier);
    }

    [Fact]
    public async Task RequestCompleteMission_PostsBase64FormAndReturnsDecodedBody() {
        var resp = new CompleteMissionResponse { Success = true };
        byte[] respBin = Serialize(resp);

        var handler = new CapturingHandler(Convert.ToBase64String(respBin));
        var client = EggLedgerApi.Create(handler);

        var expectedReq = new MissionRequest {
            Rinfo = client.NewBasicRequestInfo("EI42"),
            EiUserId = "EI42",
            Info = new MissionInfo { Identifier = "m1" },
            ClientVersion = ApiClient.DefaultClientVersion,
        };
        string expectedReqBase64 = Convert.ToBase64String(Serialize(expectedReq));

        byte[] decoded = await client.RequestCompleteMissionRawPayloadAsync("EI42", "m1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://ctx-dot-auxbrainhome.appspot.com/ei_afx/complete_mission",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        Assert.Equal("data=" + Uri.EscapeDataString(expectedReqBase64), handler.RequestBody);
        Assert.Equal(respBin, decoded);
    }

    [Fact]
    public async Task RequestFirstContact_PostsLedgerRequest() {
        var handler = new CapturingHandler(Convert.ToBase64String(Serialize(new EggIncFirstContactResponse())));
        var client = EggLedgerApi.Create(handler);

        var expectedReq = new EggIncFirstContactRequest {
            Rinfo = client.NewBasicRequestInfo("EI42"),
            EiUserId = "EI42",
            DeviceId = "EggLedger",
            ClientVersion = ApiClient.DefaultClientVersion,
            Platform = ApiClient.DefaultPlatform,
        };

        await client.RequestFirstContactRawPayloadAsync("EI42");

        Assert.Equal(
            "https://ctx-dot-auxbrainhome.appspot.com/ei/bot_first_contact",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("data=" + Uri.EscapeDataString(Convert.ToBase64String(Serialize(expectedReq))), handler.RequestBody);
    }

    [Fact]
    public async Task Request_ProxyOrigin_ResolvesAgainstHostBase() {
        var handler = new CapturingHandler(Convert.ToBase64String(Serialize(new CompleteMissionResponse())));
        var client = EggLedgerApi.Create(handler, "/egg-api", new Uri("https://ledger.test/"));

        await client.RequestCompleteMissionRawPayloadAsync("EI42", "m1");

        Assert.Equal("https://ledger.test/egg-api/ei_afx/complete_mission", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Request_ThrowsOnNon2xx() {
        var handler = new CapturingHandler("boom", HttpStatusCode.InternalServerError);
        var client = EggLedgerApi.Create(handler);

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.RequestFirstContactRawPayloadAsync("EI42"));
        Assert.Equal("POST https://ctx-dot-auxbrainhome.appspot.com/ei/bot_first_contact: HTTP 500: boom", ex.Message);
    }

    [Fact]
    public async Task Request_MalformedBase64_ThrowsDecodeError() {
        var client = EggLedgerApi.Create(new CapturingHandler("not*base64"));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.RequestFirstContactRawPayloadAsync("EI42"));
        Assert.Contains("base64 decode error", ex.Message);
    }

    [Fact]
    public async Task Request_CallerCancellation_IsInterrupted() {
        var client = EggLedgerApi.Create(new CapturingHandler(""));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.RequestFirstContactRawPayloadAsync("EI42", cts.Token));
        Assert.EndsWith(": interrupted", ex.Message);
    }

    [Fact]
    public async Task Request_TransportFailure_KeepsMessage() {
        var client = EggLedgerApi.Create(new ThrowingHandler(new HttpRequestException("refused")));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.RequestFirstContactRawPayloadAsync("EI42"));
        Assert.Equal("POST https://ctx-dot-auxbrainhome.appspot.com/ei/bot_first_contact: refused", ex.Message);
    }

    [Fact]
    public async Task Request_HandlerTimeout_IsTimeout() {
        var client = EggLedgerApi.Create(new ThrowingHandler(new TaskCanceledException()));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.RequestFirstContactRawPayloadAsync("EI42"));
        Assert.Contains("timeout after", ex.Message);
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(ex);
    }

    [Fact]
    public void NewBasicRequestInfo_UsesDefaultsAndProtoPlatformName() {
        var client = new ApiClient();
        var rinfo = client.NewBasicRequestInfo("EI777");

        Assert.Equal("EI777", rinfo.EiUserId);
        Assert.Equal(72u, rinfo.ClientVersion);
        Assert.Equal("1.35.7", rinfo.Version);
        Assert.Equal("111343", rinfo.Build);
        Assert.Equal("IOS", rinfo.Platform);
    }

    private sealed class CapturingHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? RequestBody { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            LastRequest = request;
            if (request.Content is not null) {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                ContentType = request.Content.Headers.ContentType?.MediaType;
            }
            return new HttpResponseMessage(status) {
                Content = new StringContent(responseBody),
            };
        }
    }
}
