using System.Text.Json;
using EggLedger.Domain.Api;
using EggLedger.Domain.Ei;
using Ei;
using Xunit;

namespace EggLedger.Domain.Tests.Api;

public class ApiPayloadJsonTests {
    [Fact]
    public void GetOnlyCollections_SurviveRoundTrip() {
        var resp = new CompleteMissionResponse { Success = true };
        resp.Artifacts.Add(new CompleteMissionResponse.SecureArtifactSpec());
        resp.Artifacts.Add(new CompleteMissionResponse.SecureArtifactSpec());

        string json = JsonSerializer.Serialize(resp, ApiPayloadJson.Options);
        var back = JsonSerializer.Deserialize<CompleteMissionResponse>(json, ApiPayloadJson.Options)!;

        Assert.Equal(2, back.Artifacts.Count);
    }
}
