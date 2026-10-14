using Ei;

namespace EggLedger.Domain.Eiafx;

public static class EiafxConfig {
    private static IEiafxConfigSource _source = GameDataEiafxConfigSource.Instance;

    public static ArtifactsConfigurationResponse Config => _source.Config;

    public static void SetSource(IEiafxConfigSource source) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Quality.ResetCache();
    }
}
