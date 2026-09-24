using Ei;
using ProtoBuf;

namespace EggLedger.Domain.MissionPacking;

public sealed class EmbeddedMissionConfigSource : IMissionConfigSource {
    private const string ResourceName = "EggLedger.Domain.Resources.eiafx-config.bin";

    private static readonly Lazy<ArtifactsConfigurationResponse> LazyConfig = new(Load);

    public ArtifactsConfigurationResponse Config => LazyConfig.Value;

    private static ArtifactsConfigurationResponse Load() {
        var asm = typeof(EmbeddedMissionConfigSource).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"embedded eiafx config resource not found: {ResourceName}");
        return Serializer.Deserialize<ArtifactsConfigurationResponse>(stream);
    }
}
