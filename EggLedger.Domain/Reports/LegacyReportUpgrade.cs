using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EggIdentity.Filters;
using EggIdentity.Reports;
using SortKey = EggIdentity.Reports.SortKey;

namespace EggLedger.Domain.Reports;

public sealed class LegacyFilterSetJsonConverter : JsonConverter<FilterSet> {
    public override FilterSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<FilterSet>(ref reader, FilterCodec.Options) ?? FilterSet.Empty;

    public override void Write(Utf8JsonWriter writer, FilterSet value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, FilterCodec.Options);
}

public sealed record LegacyReportDefinition {
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("accountId")]
    public string AccountId { get; init; } = "";
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = "";
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "";
    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; init; } = "";
    [JsonPropertyName("groupBy")]
    public string GroupBy { get; init; } = "";
    [JsonPropertyName("secondaryGroupBy")]
    public string SecondaryGroupBy { get; init; } = "";
    [JsonPropertyName("timeBucket")]
    public string TimeBucket { get; init; } = "";
    [JsonPropertyName("customBucketN")]
    public int CustomBucketN { get; init; }
    [JsonPropertyName("customBucketUnit")]
    public string CustomBucketUnit { get; init; } = "";
    [JsonPropertyName("filters")]
    [JsonConverter(typeof(LegacyFilterSetJsonConverter))]
    public FilterSet Filters { get; init; } = FilterSet.Empty;
    [JsonPropertyName("gridX")]
    public int GridX { get; init; }
    [JsonPropertyName("gridY")]
    public int GridY { get; init; }
    [JsonPropertyName("gridW")]
    public int GridW { get; init; }
    [JsonPropertyName("gridH")]
    public int GridH { get; init; }
    [JsonPropertyName("weight")]
    public string Weight { get; init; } = "";
    [JsonPropertyName("color")]
    public string Color { get; init; } = "";
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    [JsonPropertyName("chartType")]
    public string ChartType { get; init; } = "";
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; init; }
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; init; }
    [JsonPropertyName("valueFilterOp")]
    public string ValueFilterOp { get; init; } = "";
    [JsonPropertyName("valueFilterThreshold")]
    public double ValueFilterThreshold { get; init; }
    [JsonPropertyName("groupId")]
    public string GroupId { get; init; } = "";
    [JsonPropertyName("normalizeBy")]
    public string NormalizeBy { get; init; } = "";
    [JsonPropertyName("labelColors")]
    public string LabelColors { get; init; } = "";
    [JsonPropertyName("unfilledColor")]
    public string UnfilledColor { get; init; } = "";
    [JsonPropertyName("familyWeight")]
    public string FamilyWeight { get; init; } = "";
    [JsonPropertyName("mennoEnabled")]
    public bool MennoEnabled { get; init; }
    [JsonPropertyName("mennoCompareMode")]
    public string MennoCompareMode { get; init; } = "";
    [JsonPropertyName("minSampleSize")]
    public int MinSampleSize { get; init; }
}

public static class LegacyReportUpgrade {
    public static ReportUpgrades Upgrades { get; } = new ReportUpgrades().Add(0, Step);

    public static LegacySplit Step(JsonObject legacy) {
        ArgumentNullException.ThrowIfNull(legacy);
        var flat = legacy.Deserialize<LegacyReportDefinition>() ?? new LegacyReportDefinition();
        var (definition, placement) = Convert(flat);
        var node = JsonSerializer.SerializeToNode(definition, ReportCodec.Options)!.AsObject();
        node["version"] = 1;
        return new LegacySplit(node, placement);
    }

    public static (EggIdentity.Reports.ReportDefinition Definition, ReportPlacement Placement) Convert(LegacyReportDefinition legacy) {
        ArgumentNullException.ThrowIfNull(legacy);

        var primaryKey = MapGroupBy(legacy.GroupBy);
        var secondaryKey = MapGroupBy(legacy.SecondaryGroupBy);
        var source = MapSource(legacy.Subject);
        var and = new List<FilterCondition>(legacy.Filters.And);

        List<GroupKey> groupBy;
        if (legacy.Mode == "time_series") {
            var bucket = MapBucket(legacy.TimeBucket, legacy.CustomBucketUnit);
            groupBy = [new GroupKey(LedgerReportKeys.Launched, bucket)];
            if (secondaryKey is not null) groupBy.Add(new GroupKey(secondaryKey));
            if (legacy.TimeBucket == "custom" && legacy.CustomBucketN > 0) {
                and.Add(new FilterCondition(LedgerReportKeys.Launched, FilterOps.Within,
                    $"{legacy.CustomBucketN}{CustomUnitLetter(legacy.CustomBucketUnit)}"));
            }
        } else {
            groupBy = [];
            if (primaryKey is not null) groupBy.Add(new GroupKey(primaryKey));
            if (secondaryKey is not null) groupBy.Add(new GroupKey(secondaryKey));
        }

        List<MeasureRequest> measures;
        if (legacy.Subject == LedgerReportKeys.Fuel) {
            measures = [new MeasureRequest(LedgerReportKeys.FuelAmount, Aggregation.Sum)];
        } else if (legacy.FamilyWeight != "") {
            measures = [new MeasureRequest(LedgerReportKeys.CraftingWeight, Aggregation.Sum)];
            and.Add(new FilterCondition(LedgerReportKeys.Family, FilterOps.Eq, legacy.FamilyWeight));
        } else {
            measures = [];
        }
        var measureKey = measures.Count > 0 ? measures[0].Key : MeasureRequest.RowCountKey;

        List<SortKey> sort = legacy.Mode != "time_series" && secondaryKey is null
            ? [new SortKey(measureKey, true)]
            : [];

        var filters = legacy.Filters with { And = and };

        var normalize = legacy.NormalizeBy switch {
            "row_pct" => Normalize.Row,
            "col_pct" => Normalize.Column,
            "global_pct" => Normalize.Global,
            "airtime" => Normalize.By(LedgerReportKeys.Airtime),
            "launches" => Normalize.By(LedgerReportKeys.Launches),
            _ => Normalize.None,
        };

        List<HavingClause> having = legacy.ValueFilterOp != ""
            ? [new HavingClause(measureKey, legacy.ValueFilterOp, legacy.ValueFilterThreshold)]
            : [];

        var query = new ReportQuery(source) {
            GroupBy = groupBy,
            Measures = measures,
            Filters = filters,
            Normalize = normalize,
            Sort = sort,
            Having = having,
            MinSample = legacy.MinSampleSize > 0 ? legacy.MinSampleSize : null,
        };

        var chart = legacy.DisplayMode switch {
            "heatmap" => ReportCharts.Heatmap,
            "grouped_bar" => ReportCharts.GroupedBar,
            "multi_line" => ReportCharts.MultiLine,
            "pie" => ReportCharts.Pie,
            "line" => ReportCharts.Line,
            "grid" or "table" => ReportCharts.Table,
            _ => ReportCharts.Bar,
        };

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (legacy.DisplayMode != "") options[LedgerReportKeys.DisplayModeOption] = legacy.DisplayMode;
        if (legacy.MennoEnabled) {
            options[LedgerReportKeys.MennoOption] = "true";
            if (legacy.MennoCompareMode != "") options[LedgerReportKeys.MennoCompareOption] = legacy.MennoCompareMode;
        }

        var view = new ReportView(chart) {
            Sample = legacy.MinSampleSize > 0 ? SampleRule.Dim : SampleRule.Show,
            Color = ReportCodec.IsHex(legacy.Color) ? legacy.Color : null,
            Unfilled = ReportCodec.IsHex(legacy.UnfilledColor) ? legacy.UnfilledColor : null,
            LabelColors = BuildLabelColors(legacy.LabelColors, legacy.Mode, legacy.GroupBy),
            Options = options,
        };

        var definition = new EggIdentity.Reports.ReportDefinition(
            ReportCodec.CurrentVersion,
            legacy.Id,
            legacy.Name,
            query,
            view) {
            Description = legacy.Description == "" ? null : legacy.Description,
        };

        var placement = new ReportPlacement(
            legacy.Id,
            legacy.GridX,
            legacy.GridY,
            legacy.GridW > 0 ? legacy.GridW : 2,
            legacy.GridH > 0 ? legacy.GridH : 2,
            legacy.SortOrder) {
            Group = legacy.GroupId == "" ? null : legacy.GroupId,
        };

        return (definition, placement);
    }

    private static string? MapGroupBy(string legacyName) => legacyName switch {
        "ship_type" => LedgerReportKeys.Ship,
        "duration_type" => LedgerReportKeys.Duration,
        "level" => LedgerReportKeys.Level,
        "mission_type" => LedgerReportKeys.MissionType,
        "mission_target" => LedgerReportKeys.Target,
        "artifact_name" => LedgerReportKeys.ArtifactName,
        "rarity" => LedgerReportKeys.Rarity,
        "tier" => LedgerReportKeys.Tier,
        "spec_type" => LedgerReportKeys.SpecType,
        _ => null,
    };

    private static string MapSource(string subject) => subject switch {
        "ships" => LedgerReportKeys.Missions,
        "" => LedgerReportKeys.Missions,
        _ => subject,
    };

    private static EggIdentity.Reports.TimeBucket MapBucket(string timeBucket, string customUnit) => timeBucket switch {
        "day" => EggIdentity.Reports.TimeBucket.Day,
        "week" => EggIdentity.Reports.TimeBucket.Week,
        "month" => EggIdentity.Reports.TimeBucket.Month,
        "year" => EggIdentity.Reports.TimeBucket.Year,
        "custom" => MapCustomUnit(customUnit),
        _ => EggIdentity.Reports.TimeBucket.Month,
    };

    private static EggIdentity.Reports.TimeBucket MapCustomUnit(string unit) => unit switch {
        "week" => EggIdentity.Reports.TimeBucket.Week,
        "month" => EggIdentity.Reports.TimeBucket.Month,
        _ => EggIdentity.Reports.TimeBucket.Day,
    };

    private static string CustomUnitLetter(string unit) => unit switch {
        "week" => "w",
        "month" => "m",
        _ => "d",
    };

    private static IEnumerable<int> LabelCandidates(string groupBy) => groupBy switch {
        "ship_type" or "duration_type" or "mission_type" or "rarity" or "tier" or "level" => Enumerable.Range(0, 65),
        "artifact_name" or "mission_target" => Enumerable.Range(-1, 202),
        _ => [],
    };

    private static Dictionary<string, string> BuildLabelColors(string legacyLabelColors, string mode, string groupBy) {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(legacyLabelColors)) return result;

        JsonObject? obj;
        try {
            obj = JsonNode.Parse(legacyLabelColors) as JsonObject;
        } catch (JsonException) {
            return result;
        }
        if (obj is null) return result;

        foreach (var (label, node) in obj) {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var hex) || !ReportCodec.IsHex(hex)) continue;
            if (mode == "time_series") {
                result[label] = hex;
                continue;
            }
            if (groupBy == "spec_type") {
                if (Labels.FormatLabel(groupBy, label) == label) result[label] = hex;
                continue;
            }
            foreach (var candidate in LabelCandidates(groupBy)) {
                var raw = candidate.ToString(CultureInfo.InvariantCulture);
                if (Labels.FormatLabel(groupBy, raw) != label) continue;
                result[raw] = hex;
                break;
            }
        }
        return result;
    }
}
