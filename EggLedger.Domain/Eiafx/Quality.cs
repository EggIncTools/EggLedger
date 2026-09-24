using Ei;

namespace EggLedger.Domain.Eiafx;

public static class Quality {
    private readonly record struct SpecKey(
        ArtifactSpec.Name Name,
        ArtifactSpec.Level Level,
        ArtifactSpec.Rarity Rarity);

    private static readonly Lock Gate = new();
    private static Dictionary<SpecKey, double>? _baseQuality;

    public static double BaseQualityFor(ArtifactSpec spec) {
        ArgumentNullException.ThrowIfNull(spec);
        var map = Map();
        return map.TryGetValue(new SpecKey(spec.name, spec.level, spec.rarity), out var q) ? q : 0d;
    }

    internal static void ResetCache() {
        lock (Gate) {
            _baseQuality = null;
        }
    }

    private static Dictionary<SpecKey, double> Map() {
        if (_baseQuality is { } existing) {
            return existing;
        }

        lock (Gate) {
            if (_baseQuality is { } locked) {
                return locked;
            }

            var parameters = EiafxConfig.Config.artifact_parameters;
            var m = new Dictionary<SpecKey, double>(parameters.Count);
            foreach (var art in parameters) {
                if (art.Spec is not { } s) {
                    continue;
                }
                m[new SpecKey(s.name, s.level, s.rarity)] = art.BaseQuality;
            }
            _baseQuality = m;
            return m;
        }
    }
}
