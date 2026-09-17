using System.Text.Json.Serialization;
using EggIdentity.Contract;

namespace EggLedger.Web.Server.Sync.Verify;

public sealed record VerifyPayload(
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("built")] string Built);

public static class VerifyEndpoint {
    public static VerifyPayload Payload(VerifyInfo info) => new(info.Sha256, info.Version, info.Date);

    public static void Map(IEndpointRouteBuilder app, VerifyInfo build) =>
        app.MapGet("/api/v1/verify", () => Results.Json(Payload(build)));
}
