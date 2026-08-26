using System.Text.Json;
using System.Text.Json.Serialization;
using EggLedger.Domain.Reports;

namespace EggLedger.Web.Data;

public static class Rows {
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed record MissionRow {
    [JsonPropertyName("player_id")]
    public string PlayerId { get; init; } = "";
    [JsonPropertyName("mission_id")]
    public string MissionId { get; init; } = "";
    [JsonPropertyName("start_timestamp")]
    public double StartTimestamp { get; init; }

    [JsonPropertyName("complete_payload")]
    public byte[] CompletePayload { get; init; } = [];
    [JsonPropertyName("mission_type")]
    public int MissionType { get; init; }
    [JsonPropertyName("ship")]
    public int Ship { get; init; }
    [JsonPropertyName("duration_type")]
    public int DurationType { get; init; }
    [JsonPropertyName("level")]
    public int Level { get; init; }
    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }
    [JsonPropertyName("is_dub_cap")]
    public bool IsDubCap { get; init; }
    [JsonPropertyName("is_bugged_cap")]
    public bool IsBuggedCap { get; init; }
    [JsonPropertyName("target")]
    public int Target { get; init; }
    [JsonPropertyName("return_timestamp")]
    public double ReturnTimestamp { get; init; }
    [JsonPropertyName("nominal_capacity")]
    public int NominalCapacity { get; init; }
}


public sealed record MissionMetaRow {
    [JsonPropertyName("player_id")] public string PlayerId { get; init; } = "";
    [JsonPropertyName("mission_id")] public string MissionId { get; init; } = "";
    [JsonPropertyName("start_timestamp")] public double StartTimestamp { get; init; }
    [JsonPropertyName("mission_type")] public int MissionType { get; init; }
    [JsonPropertyName("ship")] public int Ship { get; init; }
    [JsonPropertyName("duration_type")] public int DurationType { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("capacity")] public int Capacity { get; init; }
    [JsonPropertyName("is_dub_cap")] public bool IsDubCap { get; init; }
    [JsonPropertyName("is_bugged_cap")] public bool IsBuggedCap { get; init; }
    [JsonPropertyName("target")] public int Target { get; init; }
    [JsonPropertyName("return_timestamp")] public double ReturnTimestamp { get; init; }
    [JsonPropertyName("nominal_capacity")] public int NominalCapacity { get; init; }
}

public sealed record InFlightMissionRow {
    [JsonPropertyName("player_id")]
    public string PlayerId { get; init; } = "";
    [JsonPropertyName("mission_id")]
    public string MissionId { get; init; } = "";
    [JsonPropertyName("captured_at")]
    public long CapturedAt { get; init; }
    [JsonPropertyName("payload")]
    public string Payload { get; init; } = "";
}

public sealed record BackupRow {
    [JsonPropertyName("player_id")]
    public string PlayerId { get; init; } = "";
    [JsonPropertyName("backed_up_at")]
    public double RecordedAt { get; init; }
    [JsonPropertyName("payload")]
    public byte[] Payload { get; init; } = [];
}

public sealed record ArtifactDropRow {
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Id { get; init; }
    [JsonPropertyName("mission_id")]
    public string MissionId { get; init; } = "";
    [JsonPropertyName("player_id")]
    public string PlayerId { get; init; } = "";
    [JsonPropertyName("drop_index")]
    public int DropIndex { get; init; }
    [JsonPropertyName("artifact_id")]
    public int ArtifactId { get; init; }
    [JsonPropertyName("spec_type")]
    public string SpecType { get; init; } = "";
    [JsonPropertyName("level")]
    public int Level { get; init; }
    [JsonPropertyName("rarity")]
    public int Rarity { get; init; }
    [JsonPropertyName("quality")]
    public double Quality { get; init; }
}

public sealed record SettingRow {
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}

public sealed record ReportRow {
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("account_id")]
    public string AccountId { get; init; } = "";
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = "";
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "";
    [JsonPropertyName("display_mode")]
    public string DisplayMode { get; init; } = "";
    [JsonPropertyName("group_by")]
    public string GroupBy { get; init; } = "";
    [JsonPropertyName("time_bucket")]
    public string? TimeBucket { get; init; }
    [JsonPropertyName("custom_bucket_n")]
    public int? CustomBucketN { get; init; }
    [JsonPropertyName("custom_bucket_unit")]
    public string? CustomBucketUnit { get; init; }
    [JsonPropertyName("filters")]
    public string Filters { get; init; } = "{\"and\":[],\"or\":[]}";
    [JsonPropertyName("grid_x")]
    public int GridX { get; init; }
    [JsonPropertyName("grid_y")]
    public int GridY { get; init; }
    [JsonPropertyName("grid_w")]
    public int GridW { get; init; }
    [JsonPropertyName("grid_h")]
    public int GridH { get; init; }
    [JsonPropertyName("weight")]
    public string Weight { get; init; } = "LOW";
    [JsonPropertyName("sort_order")]
    public int SortOrder { get; init; }
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }
    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; init; }
    [JsonPropertyName("color")]
    public string Color { get; init; } = "#6366f1";
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    [JsonPropertyName("chart_type")]
    public string ChartType { get; init; } = "bar";
    [JsonPropertyName("value_filter_op")]
    public string ValueFilterOp { get; init; } = "";
    [JsonPropertyName("value_filter_threshold")]
    public double ValueFilterThreshold { get; init; }
    [JsonPropertyName("group_id")]
    public string GroupId { get; init; } = "";
    [JsonPropertyName("normalize_by")]
    public string NormalizeBy { get; init; } = ReportDefaults.NormalizeNone;
    [JsonPropertyName("label_colors")]
    public string LabelColors { get; init; } = "";
    [JsonPropertyName("secondary_group_by")]
    public string SecondaryGroupBy { get; init; } = "";
    [JsonPropertyName("unfilled_color")]
    public string UnfilledColor { get; init; } = "";
    [JsonPropertyName("family_weight")]
    public string FamilyWeight { get; init; } = "";
    [JsonPropertyName("menno_enabled")]
    public bool MennoEnabled { get; init; }
    [JsonPropertyName("menno_compare_mode")]
    public string MennoCompareMode { get; init; } = "side_by_side";
    [JsonPropertyName("min_sample_size")]
    public int MinSampleSize { get; init; }
}

public sealed record ReportGroupRow {
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("account_id")]
    public string AccountId { get; init; } = "";
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("sort_order")]
    public int SortOrder { get; init; }
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }
}
