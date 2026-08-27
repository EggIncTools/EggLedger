using System.IO.Compression;
using System.Net;
using System.Text;
using EggLedger.Domain.Reports;
using EggLedger.Web.Services;

namespace EggLedger.Web.Tests.Services;

public sealed class MennoServiceTests {
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "menno");

    private static byte[] FixtureBytes(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDir, name));

    private static byte[] Gzip(byte[] raw) {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true)) {
            gz.Write(raw, 0, raw.Length);
        }
        return ms.ToArray();
    }


    private sealed class GzipHandler : HttpMessageHandler {
        private readonly byte[] _gzipped;
        public int Hits;
        public GzipHandler(byte[] rawBody) => _gzipped = Gzip(rawBody);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Interlocked.Increment(ref Hits);
            var resp = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(_gzipped),
            };
            return Task.FromResult(resp);
        }
    }

    private static MennoService Make(byte[] rawBody, out GzipHandler handler) {
        handler = new GzipHandler(rawBody);
        var http = new HttpClient(handler);
        return new MennoService(http);
    }

    [Fact]
    public async Task Refresh_DecodesFixture_PopulatesTypedFields() {
        var service = Make(FixtureBytes("menno-sample.json"), out var handler);

        var items = await service.RefreshAsync();

        Assert.Equal(1, handler.Hits);
        Assert.Equal(6, items.Count);
        Assert.True(service.HasData);

        var first = items[0];
        Assert.NotNull(first.ShipConfiguration);
        Assert.Equal(9, first.ShipConfiguration!.ShipType!.Id);
        Assert.Equal("Henerprise", first.ShipConfiguration.ShipType.Name);
        Assert.Equal(0, first.ShipConfiguration.ShipDurationType!.Id);
        Assert.Equal(0, first.ShipConfiguration.Level);
        Assert.Equal(10000, first.ShipConfiguration.TargetArtifact!.Id);
        Assert.Equal(1, first.ArtifactConfiguration!.ArtifactType!.Id);
        Assert.Equal(0, first.ArtifactConfiguration.ArtifactRarity!.Id);
        Assert.Equal(0, first.ArtifactConfiguration.ArtifactLevel);
        Assert.Equal(900, first.TotalDrops);
    }

    [Fact]
    public void Decode_MissingRequiredNestedField_ThrowsLoudly() {


        const string drifted = """
        [
          {
            "shipConfiguration": {
              "shipTypeRenamed": { "id": 9, "name": "Henerprise" },
              "shipDurationType": { "id": 0, "name": "Short" },
              "level": 0,
              "targetArtifact": { "id": 10000, "name": "None" }
            },
            "artifactConfiguration": {
              "artifactType": { "id": 1, "name": "x" },
              "artifactRarity": { "id": 0, "name": "Common" },
              "artifactLevel": 0
            },
            "totalDrops": 900
          }
        ]
        """;
        var ex = Assert.Throws<MennoSchemaException>(
            () => MennoDecode.Decode(Encoding.UTF8.GetBytes(drifted)));
        Assert.Contains("shipType", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_EmptyArray_ThrowsLoudly() {
        var ex = Assert.Throws<MennoSchemaException>(
            () => MennoDecode.Decode(Encoding.UTF8.GetBytes("[]")));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_NotAnArray_ThrowsLoudly() {

        Assert.Throws<MennoSchemaException>(
            () => MennoDecode.Decode(Encoding.UTF8.GetBytes("{\"configurationItems\":[]}")));
    }

    [Fact]
    public async Task Refresh_DriftedPayload_ThrowsAndLeavesCacheEmpty() {
        const string drifted = """
        [ { "shipConfiguration": null, "artifactConfiguration": null, "totalDrops": 5 } ]
        """;
        var service = Make(Encoding.UTF8.GetBytes(drifted), out _);

        await Assert.ThrowsAsync<MennoSchemaException>(() => service.RefreshAsync());
        Assert.False(service.HasData);
        Assert.Null(service.CachedItems);
    }

    private static ReportDefinition ShipDurationDef(string normalizeBy = "", string familyWeight = "") => new() {
        MennoEnabled = true,
        Subject = "artifacts",
        GroupBy = "ship_type",
        SecondaryGroupBy = "duration_type",
        NormalizeBy = normalizeBy,
        FamilyWeight = familyWeight,
        Weight = "gold",
    };

    [Fact]
    public async Task ExecuteComparison_ShipByDuration_GoldenMatrixAndAirtime() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var def = ShipDurationDef();
        var result = MennoService.ExecuteComparison(
            def, items, new[] { "9", "10" }, new[] { "0", "1" });

        Assert.NotNull(result);
        Assert.True(result!.Is2D);
        Assert.True(result.IsFloat);
        Assert.Equal("gold", result.Weight);
        Assert.Equal(new[] { "9", "10" }, result.RawRowLabels);
        Assert.Equal(new[] { "0", "1" }, result.RawColLabels);


        var expected = new[] { 70.0, 62.0, 60.0, 78.0 };
        Assert.Equal(expected.Length, result.MatrixValues.Count);
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i], result.MatrixValues[i], 9);
        }


        Assert.NotNull(result.AirtimeMatrixValues);
        var expectedAir = new[] { 70.0 / 24, 62.0 / 48, 60.0 / 48, 78.0 / 72 };
        for (int i = 0; i < expectedAir.Length; i++) {
            Assert.Equal(expectedAir[i], result.AirtimeMatrixValues![i], 9);
        }
    }

    [Fact]
    public async Task ExecuteComparison_RowPct_NormalizesAndSkipsAirtime() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var def = ShipDurationDef(normalizeBy: "row_pct");
        var result = MennoService.ExecuteComparison(
            def, items, new[] { "9", "10" }, new[] { "0", "1" });

        Assert.NotNull(result);


        var expected = new[]
        {
            2800.0 / 3420 * 100, 620.0 / 3420 * 100,
            1200.0 / 1980 * 100, 780.0 / 1980 * 100,
        };
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i], result!.MatrixValues[i], 9);
        }

        Assert.Null(result!.AirtimeMatrixValues);
    }

    [Fact]
    public async Task ExecuteComparison_FormatsDisplayLabels() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var result = MennoService.ExecuteComparison(
            ShipDurationDef(), items, new[] { "9", "10" }, new[] { "0", "1" });

        Assert.NotNull(result);

        Assert.Equal(2, result!.RowLabels.Count);
        Assert.Equal(2, result.ColLabels.Count);
        Assert.DoesNotContain("9", result.RowLabels[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "artifacts", "ship_type", "duration_type")]
    [InlineData(true, "ships", "ship_type", "duration_type")]
    [InlineData(true, "artifacts", "spec_type", "duration_type")]
    [InlineData(true, "artifacts", "ship_type", "")]
    public async Task ExecuteComparison_IneligibleReport_ReturnsNull(
        bool enabled, string subject, string groupBy, string secondary) {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var def = new ReportDefinition {
            MennoEnabled = enabled,
            Subject = subject,
            GroupBy = groupBy,
            SecondaryGroupBy = secondary,
        };
        var result = MennoService.ExecuteComparison(
            def, items, new[] { "9", "10" }, new[] { "0", "1" });

        Assert.Null(result);
    }

    [Fact]
    public void ExecuteComparison_EmptyInputs_ReturnsNull() {
        var def = ShipDurationDef();
        Assert.Null(MennoService.ExecuteComparison(
            def, Array.Empty<ConfigurationItem>(), new[] { "9" }, new[] { "0" }));
    }

    [Fact]
    public async Task ExecuteComparison_FilterOnNonAxisField_ExcludesNonMatchingItems() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var baseline = MennoService.ExecuteComparison(
            ShipDurationDef(), items, new[] { "9" }, new[] { "0" });
        Assert.NotEqual(0.0, baseline!.MatrixValues[0]);

        var def = ShipDurationDef();
        def.Filters = new ReportFilters {
            And = [new FilterCondition { TopLevel = "target", Op = "=", Val = "99999" }],
        };

        var result = MennoService.ExecuteComparison(
            def, items, new[] { "9" }, new[] { "0" });

        Assert.NotNull(result);
        Assert.Equal(0.0, result!.MatrixValues[0], 9);
    }

    [Fact]
    public async Task ExecuteComparison_FilterOnScopeOutsideGroupByAxes_RestrictsMatch() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var def = new ReportDefinition {
            MennoEnabled = true,
            Subject = "artifacts",
            GroupBy = "artifact_name",
            SecondaryGroupBy = "rarity",
            Weight = "gold",
            Filters = new ReportFilters {
                And = [new FilterCondition { TopLevel = "ship", Op = "=", Val = "10" }],
            },
        };

        var result = MennoService.ExecuteComparison(
            def, items, new[] { "1" }, new[] { "0" });

        Assert.NotNull(result);
        Assert.Equal(0.0, result!.MatrixValues[0], 9);
    }
}
