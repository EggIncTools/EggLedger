namespace EggLedger.Web.Server.Ships;

public sealed record GlbResult(int Status, string? ContentType, byte[]? Bytes);

public static class ShipEndpoints {
    public static async Task<GlbResult> HandleGlb(
        ShipAssetService svc, bool isAdmin, string key, CancellationToken ct) {
        if (!isAdmin) return new GlbResult(403, null, null);
        var bytes = await svc.ReadAsync(key, ct).ConfigureAwait(false);
        return bytes is null
            ? new GlbResult(404, null, null)
            : new GlbResult(200, "model/gltf-binary", bytes);
    }
}
