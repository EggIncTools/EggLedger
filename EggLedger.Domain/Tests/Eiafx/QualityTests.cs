using System.Globalization;
using EggLedger.Domain.Eiafx;
using Ei;

namespace EggLedger.Domain.Tests.Eiafx;

public class QualityTests {
    [Fact]
    public void BaseQualityFor_FreshSpecMatchesByValue() {
        var parameters = EiafxConfig.Config.artifact_parameters;
        Assert.NotEmpty(parameters);

        var sample = parameters.FirstOrDefault(p => p.BaseQuality > 0);
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
    public void BaseQualityFor_MatchesGoldenFields() {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "eiafx-config-fixture.fields");
        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);

        foreach (var line in lines) {
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }
            var cols = line.Split(',');
            Assert.Equal(4, cols.Length);

            var spec = new ArtifactSpec {
                name = (ArtifactSpec.Name)int.Parse(cols[0], CultureInfo.InvariantCulture),
                level = (ArtifactSpec.Level)int.Parse(cols[1], CultureInfo.InvariantCulture),
                rarity = (ArtifactSpec.Rarity)int.Parse(cols[2], CultureInfo.InvariantCulture),
            };
            var want = double.Parse(cols[3], CultureInfo.InvariantCulture);

            Assert.Equal(want, Quality.BaseQualityFor(spec), 6);
        }
    }
}
