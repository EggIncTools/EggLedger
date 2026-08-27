using System.Text.Json;
using EggLedger.Domain.MissionQuery;
using EggLedger.Domain.Reports;
using EggLedger.Web.Data;
using EggLedger.Web.Settings;

namespace EggLedger.Web.Tests.Settings;

public sealed class CloudSyncBlobsTests {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BlobNames_MatchGo() {
        Assert.Equal("accounts", CloudSyncBlobs.AccountsBlob);
        Assert.Equal("settings", CloudSyncBlobs.SettingsBlob);
        Assert.Equal("reports", CloudSyncBlobs.ReportsBlob);
    }

    [Fact]
    public void PackSettings_EmptyMap_UsesGoDefaults() {
        var s = CloudSyncBlobs.PackSettings(new Dictionary<string, string>());
        Assert.False(s.AutoRefreshMennoPref);
        Assert.Equal(1, s.WorkerCount);
        Assert.True(s.ShowMissionProgress);
        Assert.True(s.MissionViewTimes);
        Assert.True(s.MissionShowExpectedDrops);
        Assert.Equal("off", s.MissionMultiViewMode);
        Assert.Equal("default", s.MissionSortMethod);
        Assert.Equal("", s.LifetimeSortMethod);
    }

    [Fact]
    public void PackSettings_ReadsValues() {
        var map = new Dictionary<string, string> {
            ["auto_refresh_menno_pref"] = "true",
            ["worker_count"] = "8",
            ["mission_multi_view_mode"] = "row",
            ["mission_sort_method"] = "iv",
            ["lifetime_sort_method"] = "count",
            ["lifetime_show_drops_per_ship"] = "true",
        };
        var s = CloudSyncBlobs.PackSettings(map);
        Assert.True(s.AutoRefreshMennoPref);
        Assert.Equal(8, s.WorkerCount);
        Assert.Equal("row", s.MissionMultiViewMode);
        Assert.Equal("iv", s.MissionSortMethod);
        Assert.Equal("count", s.LifetimeSortMethod);
        Assert.True(s.LifetimeShowDropsPerShip);
    }

    [Fact]
    public void SyncableSettings_JsonNames_MatchGoContract() {
        var s = new CloudSyncableSettings {
            WorkerCount = 3,
            MissionMultiViewMode = "free",
            MissionSortMethod = "iv",
            LifetimeSortMethod = "iv",
        };
        var json = JsonSerializer.Serialize(s, Json);


        foreach (var key in new[]
        {
            "auto_refresh_menno_pref",
            "worker_count", "screenshot_safety", "show_mission_progress",
            "advanced_drop_filter", "mission_view_by_date",
            "mission_view_times", "mission_recolor_dc", "mission_recolor_bc",
            "mission_show_expected_drops", "mission_multi_view_mode", "mission_sort_method",
            "lifetime_sort_method", "lifetime_show_drops_per_ship", "lifetime_show_expected_totals",
        }) {
            Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pack_Then_Unpack_RoundTrips() {
        var map = new Dictionary<string, string> {
            ["auto_refresh_menno_pref"] = "true",
            ["worker_count"] = "6",
            ["show_mission_progress"] = "false",
            ["mission_multi_view_mode"] = "free",
            ["mission_sort_method"] = "iv",
            ["lifetime_sort_method"] = "count",
        };
        var packed = CloudSyncBlobs.PackSettings(map);
        var unpacked = CloudSyncBlobs.UnpackSettings(packed);

        Assert.Equal("true", unpacked["auto_refresh_menno_pref"]);
        Assert.Equal("6", unpacked["worker_count"]);
        Assert.Equal("false", unpacked["show_mission_progress"]);
        Assert.Equal("free", unpacked["mission_multi_view_mode"]);
        Assert.Equal("iv", unpacked["mission_sort_method"]);
        Assert.Equal("count", unpacked["lifetime_sort_method"]);
    }

    [Fact]
    public void UnpackSettings_OmitsMachineLocalKeys() {
        var unpacked = CloudSyncBlobs.UnpackSettings(new CloudSyncableSettings());

        Assert.False(unpacked.ContainsKey("default_resolution_x"));
        Assert.False(unpacked.ContainsKey("preferred_chromium_path"));
        Assert.False(unpacked.ContainsKey("auto_export_csv"));
        Assert.False(unpacked.ContainsKey("start_in_fullscreen"));
    }

    [Fact]
    public void SelectReportsToImport_SkipsExistingAndBlankAndDuplicates() {
        var remote = new CloudReportsBlob {
            Groups =
            [
                new CloudReportGroup { Id = "g1" },
                new CloudReportGroup { Id = "g2" },

                new CloudReportGroup { Id = "g2" },

                new CloudReportGroup { Id = "" },
            ],
            Reports =
            [
                new ReportDefinition { Id = "r1" },

                new ReportDefinition { Id = "r2" },

                new ReportDefinition { Id = "" },
            ],
        };

        var (groups, reports) = CloudSyncBlobs.SelectReportsToImport(
            remote,
            existingGroupIds: ["g1"],
            existingReportIds: ["r2"]);

        Assert.Equal(new[] { "g2" }, groups.Select(g => g.Id));
        Assert.Equal(new[] { "r1" }, reports.Select(r => r.Id));
    }

    [Fact]
    public void ReportsBlob_SerializesGoWireShape() {
        var row = new ReportRow {
            Id = "rep1",
            AccountId = "EI42",
            Name = "Eggs over time",
            GroupBy = "ship_type",
            DisplayMode = "chart",
            ValueFilterOp = "gte",
            Filters = "{\"and\":[{\"topLevel\":\"ship\",\"op\":\"eq\",\"val\":\"henerprise\"}],\"or\":[]}",
        };
        var group = new ReportGroupRow {
            Id = "grp1",
            AccountId = "EI42",
            Name = "Favorites",
            SortOrder = 3,
            CreatedAt = 1700000000,
        };

        var blob = CloudReportsBlob.Pack([row], [group]);
        var json = JsonSerializer.Serialize(blob, Json);


        Assert.Contains("\"reports\"", json, StringComparison.Ordinal);
        Assert.Contains("\"groups\"", json, StringComparison.Ordinal);


        Assert.Contains("\"accountId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"displayMode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"groupBy\"", json, StringComparison.Ordinal);
        Assert.Contains("\"valueFilterOp\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"account_id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"display_mode\"", json, StringComparison.Ordinal);


        Assert.Contains("\"filters\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"and\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"or\":[", json, StringComparison.Ordinal);
        Assert.Contains("\"topLevel\":\"ship\"", json, StringComparison.Ordinal);

        Assert.DoesNotContain("\"filters\":\"{", json, StringComparison.Ordinal);


        Assert.Contains("\"Id\":\"grp1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"AccountId\":\"EI42\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SortOrder\":3", json, StringComparison.Ordinal);
        Assert.Contains("\"CreatedAt\":1700000000", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsBlob_DeserializesGoWireShape_RoundTrips() {


        const string goJson = """
        {
          "reports": [
            {
              "id": "rep1",
              "accountId": "EI42",
              "name": "Eggs over time",
              "groupBy": "ship_type",
              "displayMode": "chart",
              "filters": {
                "and": [{ "topLevel": "ship", "op": "eq", "val": "henerprise" }],
                "or": []
              }
            }
          ],
          "groups": [
            { "Id": "grp1", "AccountId": "EI42", "Name": "Favorites", "SortOrder": 3, "CreatedAt": 1700000000 }
          ]
        }
        """;

        var blob = JsonSerializer.Deserialize<CloudReportsBlob>(goJson, Json);
        Assert.NotNull(blob);

        var (groups, reports) = CloudSyncBlobs.SelectReportsToImport(
            blob!, existingGroupIds: [], existingReportIds: []);

        var r = Assert.Single(reports);
        Assert.Equal("rep1", r.Id);
        Assert.Equal("EI42", r.AccountId);
        Assert.Equal("ship_type", r.GroupBy);

        Assert.Contains("\"topLevel\":\"ship\"", r.Filters, StringComparison.Ordinal);
        Assert.Contains("\"op\":\"eq\"", r.Filters, StringComparison.Ordinal);
        Assert.Contains("\"val\":\"henerprise\"", r.Filters, StringComparison.Ordinal);

        var g = Assert.Single(groups);
        Assert.Equal("grp1", g.Id);
        Assert.Equal("EI42", g.AccountId);
        Assert.Equal("Favorites", g.Name);
        Assert.Equal(3, g.SortOrder);
        Assert.Equal(1700000000, g.CreatedAt);
    }
}
