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
        var client = new ApiClient();

        var got = client.DecodeApiResponse<EggIncFirstContactResponse>(
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
        var client = new ApiClient();

        var got = client.DecodeApiResponse<CompleteMissionResponse>(
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

        var client = new ApiClient();
        var got = client.DecodeApiResponse<CompleteMissionResponse>(
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

        var client = new ApiClient();
        var got = client.DecodeApiResponse<CompleteMissionResponse>(
            "https://example/test", payload, authenticated: true);

        Assert.True(got.Success);
        Assert.Equal("plain", got.Info!.Identifier);
    }

    [Fact]
    public async Task RequestRawPayload_PostsBase64FormAndDecodesResponse() {
        var resp = new CompleteMissionResponse { Success = true };
        byte[] respBin = Serialize(resp);
        string respBase64 = Convert.ToBase64String(respBin);

        var handler = new CapturingHandler(respBase64);
        var http = new HttpClient(handler);
        var client = new ApiClient(http);

        var req = new MissionRequest { EiUserId = "EI42" };
        byte[] expectedReqBin = Serialize(req);
        string expectedReqBase64 = Convert.ToBase64String(expectedReqBin);

        byte[] decoded = await client.RequestRawPayloadAsync("/ei_afx/complete_mission", req);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://ctx-dot-auxbrainhome.appspot.com/ei_afx/complete_mission",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        Assert.Equal("data=" + Uri.EscapeDataString(expectedReqBase64), handler.RequestBody);

        Assert.Equal(respBin, decoded);
        var got = client.DecodeApiResponse<CompleteMissionResponse>(
            "https://example/test", decoded, authenticated: false);
        Assert.True(got.Success);
    }

    [Fact]
    public async Task RequestRawPayload_ThrowsOnNon2xx() {
        var handler = new CapturingHandler("ignored", HttpStatusCode.InternalServerError);
        var http = new HttpClient(handler);
        var client = new ApiClient(http);

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.RequestRawPayloadAsync("/ei/bot_first_contact", new MissionRequest()));
        Assert.Contains("HTTP 500", ex.Message);
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

    private sealed class CapturingHandler : HttpMessageHandler {
        private readonly string _responseBody;
        private readonly HttpStatusCode _status;

        public CapturingHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK) {
            _responseBody = responseBody;
            _status = status;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? RequestBody { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            LastRequest = request;
            if (request.Content != null) {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                ContentType = request.Content.Headers.ContentType?.MediaType;
            }
            return new HttpResponseMessage(_status) {
                Content = new StringContent(_responseBody),
            };
        }
    }
}
