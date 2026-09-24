using System.Text.Json.Serialization;

namespace EggLedger.Domain.Reports;

public sealed class FilterCondition {
    [JsonPropertyName("topLevel")]
    public string TopLevel { get; set; } = "";
    [JsonPropertyName("op")]
    public string Op { get; set; } = "";
    [JsonPropertyName("val")]
    public string Val { get; set; } = "";
}

public sealed class ReportFilters {
    [JsonPropertyName("and")]
    public List<FilterCondition> And { get; set; } = [];
    [JsonPropertyName("or")]
    public List<List<FilterCondition>> Or { get; set; } = [];
}

public sealed class ReportDefinition {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";
    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; set; } = "";
    [JsonPropertyName("groupBy")]
    public string GroupBy { get; set; } = "";
    [JsonPropertyName("secondaryGroupBy")]
    public string SecondaryGroupBy { get; set; } = "";
    [JsonPropertyName("timeBucket")]
    public string TimeBucket { get; set; } = "";
    [JsonPropertyName("customBucketN")]
    public int CustomBucketN { get; set; }
    [JsonPropertyName("customBucketUnit")]
    public string CustomBucketUnit { get; set; } = "";
    [JsonPropertyName("filters")]
    public ReportFilters Filters { get; set; } = new();
    [JsonPropertyName("gridX")]
    public int GridX { get; set; }
    [JsonPropertyName("gridY")]
    public int GridY { get; set; }
    [JsonPropertyName("gridW")]
    public int GridW { get; set; }
    [JsonPropertyName("gridH")]
    public int GridH { get; set; }
    [JsonPropertyName("weight")]
    public string Weight { get; set; } = "";
    [JsonPropertyName("color")]
    public string Color { get; set; } = "";
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    [JsonPropertyName("chartType")]
    public string ChartType { get; set; } = "";
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }
    [JsonPropertyName("valueFilterOp")]
    public string ValueFilterOp { get; set; } = "";
    [JsonPropertyName("valueFilterThreshold")]
    public double ValueFilterThreshold { get; set; }
    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = "";
    [JsonPropertyName("normalizeBy")]
    public string NormalizeBy { get; set; } = "";
    [JsonPropertyName("labelColors")]
    public string LabelColors { get; set; } = "";
    [JsonPropertyName("unfilledColor")]
    public string UnfilledColor { get; set; } = "";
    [JsonPropertyName("familyWeight")]
    public string FamilyWeight { get; set; } = "";
    [JsonPropertyName("mennoEnabled")]
    public bool MennoEnabled { get; set; }
    [JsonPropertyName("mennoCompareMode")]
    public string MennoCompareMode { get; set; } = "";
    [JsonPropertyName("minSampleSize")]
    public int MinSampleSize { get; set; }
}

public sealed class ReportResult : IEquatable<ReportResult> {
    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];
    [JsonPropertyName("values")]
    public List<long> Values { get; set; } = [];
    [JsonPropertyName("floatValues")]
    public List<double> FloatValues { get; set; } = [];
    [JsonPropertyName("isFloat")]
    public bool IsFloat { get; set; }
    [JsonPropertyName("weight")]
    public string Weight { get; set; } = "";
    [JsonPropertyName("rowLabels")]
    public List<string> RowLabels { get; set; } = [];
    [JsonPropertyName("colLabels")]
    public List<string> ColLabels { get; set; } = [];
    [JsonPropertyName("matrixValues")]
    public List<double> MatrixValues { get; set; } = [];
    [JsonPropertyName("is2D")]
    public bool Is2D { get; set; }
    [JsonPropertyName("rawRowLabels")]
    public List<string> RawRowLabels { get; set; } = [];
    [JsonPropertyName("rawColLabels")]
    public List<string> RawColLabels { get; set; } = [];
    [JsonPropertyName("rawPerMissionValues")]
    public List<double>? RawPerMissionValues { get; set; }
    [JsonPropertyName("airtimeMatrixValues")]
    public List<double>? AirtimeMatrixValues { get; set; }
    [JsonPropertyName("missionCountMatrix")]
    public List<long>? MissionCountMatrix { get; set; }

    public bool Equals(ReportResult? other) {
        if (other is null) {
            return false;
        }
        return ReferenceEquals(this, other) || IsFloat == other.IsFloat
            && Is2D == other.Is2D
            && Weight == other.Weight
            && SeqEqual(Labels, other.Labels)
            && SeqEqual(Values, other.Values)
            && SeqEqual(FloatValues, other.FloatValues)
            && SeqEqual(RowLabels, other.RowLabels)
            && SeqEqual(ColLabels, other.ColLabels)
            && SeqEqual(MatrixValues, other.MatrixValues)
            && SeqEqual(RawRowLabels, other.RawRowLabels)
            && SeqEqual(RawColLabels, other.RawColLabels)
            && NullableSeqEqual(RawPerMissionValues, other.RawPerMissionValues)
            && NullableSeqEqual(AirtimeMatrixValues, other.AirtimeMatrixValues)
            && NullableSeqEqual(MissionCountMatrix, other.MissionCountMatrix);
    }

    public override bool Equals(object? obj) => Equals(obj as ReportResult);


    public override int GetHashCode() {
        var h = new HashCode();
        h.Add(IsFloat);
        h.Add(Is2D);
        h.Add(Weight);
        h.Add(Labels.Count);
        h.Add(Values.Count);
        h.Add(FloatValues.Count);
        h.Add(MatrixValues.Count);
        return h.ToHashCode();
    }

    private static bool SeqEqual<T>(List<T> a, List<T> b) =>
        a.Count == b.Count && a.SequenceEqual(b);

    private static bool NullableSeqEqual<T>(List<T>? a, List<T>? b) {
        if (a is null) {
            return b is null;
        }
        return b is not null && a.Count == b.Count && a.SequenceEqual(b);
    }
}

public static class Report {
    public static bool MennoComparableGroupBy(string groupBy) => groupBy switch {
        "ship_type" or "duration_type" or "level" or "mission_target"
            or "artifact_name" or "rarity" or "tier" => true,
        _ => false,
    };
}
