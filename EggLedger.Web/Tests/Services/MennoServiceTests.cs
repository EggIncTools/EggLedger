using System.IO.Compression;
using System.Net;
using System.Text;
using EggIdentity.Reports;
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

    private sealed class GzipHandler(byte[] rawBody) : HttpMessageHandler {
        private readonly byte[] _gzipped = Gzip(rawBody);
        public int Hits;

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
            () => MennoDecode.Decode(Encoding.UTF8.GetBytes("""{"configurationItems":[]}""")));
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

    private static ReportDefinition Def(
        string source, string row, string col, bool enabled = true, Normalize? normalize = null, FilterSet? filters = null) {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (enabled) options[LedgerReportKeys.MennoOption] = "true";
        var query = new ReportQuery(source) {
            GroupBy = [.. new[] { row, col }.Where(d => d.Length > 0).Select(d => new GroupKey(d))],
            Normalize = normalize ?? Normalize.None,
            Filters = filters ?? FilterSet.Empty,
        };
        return new ReportDefinition(ReportCodec.CurrentVersion, "m", "Menno", query, new ReportView(ReportCharts.Heatmap) { Options = options });
    }

    private static ReportDefinition ShipDurationDef(Normalize? normalize = null, FilterSet? filters = null) =>
        Def(LedgerReportKeys.Artifacts, LedgerReportKeys.Ship, LedgerReportKeys.Duration, normalize: normalize, filters: filters);

    private static ReportMatrix User(IReadOnlyList<string> rows, IReadOnlyList<string> cols) => new(
        rows, rows, cols, cols,
        [.. rows.Select(_ => (IReadOnlyList<double?>)[.. cols.Select(_ => (double?)1)])],
        [.. rows.Select(_ => (IReadOnlyList<int>)[.. cols.Select(_ => 0)])]);

    private static double[] Flat(ReportMatrix m) => [.. m.Cells.SelectMany(r => r.Select(v => v ?? double.NaN))];

    [Fact]
    public async Task ExecuteComparison_ShipByDuration_GoldenMatrix() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var result = MennoService.ExecuteComparison(ShipDurationDef(), items, User(["9", "10"], ["0", "1"]));

        Assert.NotNull(result);
        Assert.Equal(new[] { "9", "10" }, result.RowKeys);
        Assert.Equal(new[] { "0", "1" }, result.ColKeys);
        var expected = new[] { 70.0, 62.0, 60.0, 78.0 };
        var actual = Flat(result);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 9);
    }

    [Fact]
    public async Task ExecuteComparison_AirtimeNormalize_ReturnsPerFlightHour() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var result = MennoService.ExecuteComparison(
            ShipDurationDef(Normalize.By(LedgerReportKeys.Airtime)), items, User(["9", "10"], ["0", "1"]));

        Assert.NotNull(result);
        var expected = new[] { 70.0 / 24, 62.0 / 48, 60.0 / 48, 78.0 / 72 };
        var actual = Flat(result);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 9);
    }

    [Fact]
    public async Task ExecuteComparison_RowPct_Normalizes() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var result = MennoService.ExecuteComparison(ShipDurationDef(Normalize.Row), items, User(["9", "10"], ["0", "1"]));

        Assert.NotNull(result);
        var expected = new[] {
            2800.0 / 3420 * 100, 620.0 / 3420 * 100,
            1200.0 / 1980 * 100, 780.0 / 1980 * 100,
        };
        var actual = Flat(result);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 9);
    }

    [Theory]
    [InlineData(false, LedgerReportKeys.Artifacts, LedgerReportKeys.Ship, LedgerReportKeys.Duration)]
    [InlineData(true, LedgerReportKeys.Missions, LedgerReportKeys.Ship, LedgerReportKeys.Duration)]
    [InlineData(true, LedgerReportKeys.Artifacts, LedgerReportKeys.SpecType, LedgerReportKeys.Duration)]
    [InlineData(true, LedgerReportKeys.Artifacts, LedgerReportKeys.Ship, "")]
    public async Task ExecuteComparison_IneligibleReport_ReturnsNull(bool enabled, string source, string row, string col) {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var result = MennoService.ExecuteComparison(Def(source, row, col, enabled), items, User(["9", "10"], ["0", "1"]));

        Assert.Null(result);
    }

    [Fact]
    public void ExecuteComparison_EmptyInputs_ReturnsNull() {
        Assert.Null(MennoService.ExecuteComparison(ShipDurationDef(), [], User(["9"], ["0"])));
    }

    [Fact]
    public async Task ExecuteComparison_FilterOnNonAxisField_ExcludesNonMatchingItems() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var baseline = MennoService.ExecuteComparison(ShipDurationDef(), items, User(["9"], ["0"]));
        Assert.NotNull(baseline);
        Assert.NotEqual(0.0, Flat(baseline)[0]);

        var filters = new FilterSet([new FilterCondition("target", "=", "99999")], []);
        var result = MennoService.ExecuteComparison(ShipDurationDef(filters: filters), items, User(["9"], ["0"]));

        Assert.NotNull(result);
        Assert.Equal(0.0, Flat(result)[0], 9);
    }

    [Fact]
    public async Task ExecuteComparison_FilterOnScopeOutsideGroupByAxes_RestrictsMatch() {
        var service = Make(FixtureBytes("menno-sample.json"), out _);
        var items = await service.RefreshAsync();

        var filters = new FilterSet([new FilterCondition("ship", "=", "10")], []);
        var def = Def(LedgerReportKeys.Artifacts, LedgerReportKeys.ArtifactName, LedgerReportKeys.Rarity, filters: filters);

        var result = MennoService.ExecuteComparison(def, items, User(["1"], ["0"]));

        Assert.NotNull(result);
        Assert.Equal(0.0, Flat(result)[0], 9);
    }
}
