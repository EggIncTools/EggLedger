using EggLedger.Domain.Ei;
using EggSharp.GameData;
using Ei;

namespace EggLedger.Domain.Eiafx;

public sealed class GameDataEiafxConfigSource : IEiafxConfigSource {
    public static GameDataEiafxConfigSource Instance { get; } = new();

    private static readonly Lazy<ArtifactsConfigurationResponse> LazyConfig = new(Build);

    public ArtifactsConfigurationResponse Config => LazyConfig.Value;

    internal static ArtifactsConfigurationResponse Build() {
        var docs = GameDataProvider.EmbeddedDocuments();
        return Build(
            MissionShipCatalog.Parse(docs[MissionShipCatalog.DocumentId]),
            ArtifactCatalog.Parse(docs[ArtifactCatalog.DocumentId]));
    }

    internal static ArtifactsConfigurationResponse Build(IMissionShipCatalog ships, IArtifactCatalog artifacts) {
        var config = new ArtifactsConfigurationResponse();
        foreach (var ship in ships.Ships) config.mission_parameters.Add(ToMission(ship));
        foreach (var artifact in artifacts.Artifacts) config.artifact_parameters.Add(ToArtifact(artifact));
        foreach (var level in artifacts.CraftingLevels) {
            config.CraftingLevelInfos.Add(new ArtifactsConfigurationResponse.CraftingLevelInfo {
                XpRequired = level.XpRequired,
                RarityMult = (float)level.RarityMult,
            });
        }
        return config;
    }

    private static ArtifactsConfigurationResponse.MissionParameters ToMission(MissionShipEntry ship) {
        var mission = new ArtifactsConfigurationResponse.MissionParameters {
            Ship = Parse<MissionInfo.Spaceship>(ship.Ship),
            LevelMissionRequirements = [.. ship.LevelMissionRequirements.Select(r => (uint)r)],
        };
        foreach (var d in ship.Durations) {
            mission.Durations.Add(new ArtifactsConfigurationResponse.MissionParameters.Duration {
                DurationType = Parse<MissionInfo.DurationType>(d.Type),
                Seconds = d.Seconds,
                Capacity = (uint)d.Capacity,
                LevelCapacityBump = (uint)d.LevelCapacityBump,
                Quality = (float)d.Quality,
                MinQuality = (float)d.MinQuality,
                MaxQuality = (float)d.MaxQuality,
                LevelQualityBump = (float)d.LevelQualityBump,
            });
        }
        return mission;
    }

    private static ArtifactsConfigurationResponse.ArtifactParameters ToArtifact(ArtifactCatalogEntry a) => new() {
        Spec = new ArtifactSpec {
            name = Parse<ArtifactSpec.Name>(a.SpecName),
            level = Parse<ArtifactSpec.Level>(a.Level),
            rarity = Parse<ArtifactSpec.Rarity>(a.Rarity),
        },
        BaseQuality = a.BaseQuality,
        OddsMultiplier = a.OddsMultiplier,
        Value = a.Value,
        CraftingPrice = a.CraftingPrice,
        CraftingPriceLow = a.CraftingPriceLow,
        CraftingPriceDomain = a.CraftingPriceDomain,
        CraftingPriceCurve = a.CraftingPriceCurve,
        CraftingXp = a.CraftingXp,
    };

    private static TEnum Parse<TEnum>(string protoName) where TEnum : struct, Enum =>
        EnumNames.TryValue<TEnum>(protoName, out var value)
            ? value
            : throw new InvalidOperationException($"EggSharp game data value '{protoName}' is not a {typeof(TEnum).Name}");
}
