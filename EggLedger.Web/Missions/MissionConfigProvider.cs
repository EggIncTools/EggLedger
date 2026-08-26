using EggLedger.Domain.Eiafx;
using EggLedger.Domain.MissionQuery;

namespace EggLedger.Web.Missions;

public sealed class MissionConfigProvider {
    private readonly Lazy<IReadOnlyList<PossibleMission>> _durationConfigs =
        new(() => MissionQueryHandlers.GetDurationConfigs(EiafxConfig.Config.mission_parameters));

    private readonly Lazy<double> _maxQuality = new(MissionConfigData.MaxQuality);
    private readonly Lazy<IReadOnlyList<PossibleTarget>> _targets = new(() => MissionConfigData.Targets());
    private readonly Lazy<IReadOnlyList<PossibleArtifact>> _artifacts = new(() => MissionConfigData.Artifacts());
    private readonly Lazy<FilterFieldCtx> _fieldCtx;

    public MissionConfigProvider() {
        _fieldCtx = new Lazy<FilterFieldCtx>(() => new FilterFieldCtx {
            PossibleTargets = PossibleTargets,
            ArtifactConfigs = PossibleArtifacts,
            MaxQuality = MaxQuality,
        });
    }

    public IReadOnlyList<PossibleMission> DurationConfigs => _durationConfigs.Value;
    public double MaxQuality => _maxQuality.Value;
    public IReadOnlyList<PossibleTarget> PossibleTargets => _targets.Value;
    public IReadOnlyList<PossibleArtifact> PossibleArtifacts => _artifacts.Value;

    public FilterFieldCtx FieldCtx => _fieldCtx.Value;
}
