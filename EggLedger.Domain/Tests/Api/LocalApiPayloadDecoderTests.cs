using EggLedger.Domain.Api;
using Ei;
using ProtoBuf;

namespace EggLedger.Domain.Tests.Api;

public class LocalApiPayloadDecoderTests {
    private static byte[] Serialize<T>(T msg) {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return ms.ToArray();
    }

    [Fact]
    public async Task DecodeFirstContactAsync_RoundTrips() {
        var fc = new EggIncFirstContactResponse {
            EiUserId = "EI1234567890123456",
            Backup = new Backup { EiUserId = "EI1234567890123456" },
        };
        byte[] payload = Serialize(fc);
        IApiPayloadDecoder decoder = new LocalApiPayloadDecoder(new ApiClient());

        var got = await decoder.DecodeFirstContactAsync(payload);

        Assert.Equal("EI1234567890123456", got.EiUserId);
        Assert.NotNull(got.Backup);
    }

    [Fact]
    public async Task DecodeCompleteMissionAsync_RoundTrips() {
        var inner = new CompleteMissionResponse { Success = true, EiUserId = "EI999" };
        var auth = new AuthenticatedMessage { Message = Serialize(inner), Compressed = false };
        byte[] payload = Serialize(auth);
        IApiPayloadDecoder decoder = new LocalApiPayloadDecoder(new ApiClient());

        var got = await decoder.DecodeCompleteMissionAsync(payload);

        Assert.True(got.Success);
        Assert.Equal("EI999", got.EiUserId);
    }
}
