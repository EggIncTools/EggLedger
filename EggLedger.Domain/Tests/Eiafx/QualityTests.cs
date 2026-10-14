using EggLedger.Domain.Eiafx;
using EggSharp.GameData;
using Ei;

namespace EggLedger.Domain.Tests.Eiafx;

public class QualityTests {
    [Fact]
    public void BaseQualityFor_FreshSpecMatchesByValue() {
        var parameters = EiafxConfig.Config.artifact_parameters;
        Assert.NotEmpty(parameters);

        var sample = parameters.Find(p => p.BaseQuality > 0);
        Assert.NotNull(sample);

        var s = sample!.Spec;
        var fresh = new ArtifactSpec { name = s.name, level = s.level, rarity = s.rarity };

        Assert.Equal(sample.BaseQuality, Quality.BaseQualityFor(fresh));
    }

    [Fact]
    public void BaseQualityFor_UnknownNameReturnsZero() {
        var bad = new ArtifactSpec { name = (ArtifactSpec.Name)99999 };
        Assert.Equal(0d, Quality.BaseQualityFor(bad));
    }

    [Fact]
    public void BaseQualityFor_MatchesEggSharpCatalog() {
        var catalog = ArtifactCatalog.Parse(GameDataProvider.EmbeddedDocuments()[ArtifactCatalog.DocumentId]);
        Assert.NotEmpty(catalog.Artifacts);

        foreach (var a in catalog.Artifacts) {
            var spec = new ArtifactSpec {
                name = (ArtifactSpec.Name)a.AfxId,
                level = (ArtifactSpec.Level)a.AfxLevel,
                rarity = (ArtifactSpec.Rarity)a.AfxRarity,
            };
            Assert.Equal(a.BaseQuality, Quality.BaseQualityFor(spec), 6);
        }
    }

    [Fact]
    public void LunarTotem_SpotValues() {
        var spec = new ArtifactSpec {
            name = ArtifactSpec.Name.LunarTotem,
            level = ArtifactSpec.Level.Inferior,
            rarity = ArtifactSpec.Rarity.Common,
        };
        Assert.Equal(0.7, Quality.BaseQualityFor(spec), 6);
    }
}
