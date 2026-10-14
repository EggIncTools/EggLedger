using EggLedger.Domain.Eiafx;
using Ei;

namespace EggLedger.Domain.MissionPacking;

public sealed class EmbeddedMissionConfigSource : IMissionConfigSource {
    public ArtifactsConfigurationResponse Config => GameDataEiafxConfigSource.Instance.Config;
}
