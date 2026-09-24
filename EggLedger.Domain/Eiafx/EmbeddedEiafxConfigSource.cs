using Ei;
using ProtoBuf;

namespace EggLedger.Domain.Eiafx;

public sealed class EmbeddedEiafxConfigSource : IEiafxConfigSource {
    public static EmbeddedEiafxConfigSource Instance { get; } = new();
    private const string ResourceName = "EggLedger.Domain.Resources.eiafx-config.bin";
    private static readonly Lazy<ArtifactsConfigurationResponse> LazyConfig = new(LoadEmbedded);

    public ArtifactsConfigurationResponse Config => LazyConfig.Value;

    private static ArtifactsConfigurationResponse LoadEmbedded() {
        var asm = typeof(EmbeddedEiafxConfigSource).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"embedded eiafx config resource not found: {ResourceName}");

        return Serializer.Deserialize<ArtifactsConfigurationResponse>(stream);
    }
}
