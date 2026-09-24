using System.Text.Json;
using EggLedger.Domain.Reports;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public static class ReportMapping {
    private static readonly JsonSerializerOptions FilterOptions = new(JsonSerializerDefaults.Web);

    public static ReportDefinition ToDefinition(ReportRow r, ILogger? logger = null) => new() {
        Id = r.Id,
        AccountId = r.AccountId,
        Name = r.Name,
        Subject = r.Subject,
        Mode = r.Mode,
        DisplayMode = r.DisplayMode,
        GroupBy = r.GroupBy,
        SecondaryGroupBy = r.SecondaryGroupBy,
        TimeBucket = r.TimeBucket ?? "",
        CustomBucketN = r.CustomBucketN ?? 0,
        CustomBucketUnit = r.CustomBucketUnit ?? "",
        Filters = ParseFilters(r.Filters, logger),
        GridX = r.GridX,
        GridY = r.GridY,
        GridW = r.GridW,
        GridH = r.GridH,
        Weight = r.Weight,
        Color = r.Color,
        Description = r.Description,
        ChartType = r.ChartType,
        SortOrder = r.SortOrder,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        ValueFilterOp = r.ValueFilterOp,
        ValueFilterThreshold = r.ValueFilterThreshold,
        GroupId = r.GroupId,
        NormalizeBy = r.NormalizeBy,
        LabelColors = r.LabelColors,
        UnfilledColor = r.UnfilledColor,
        FamilyWeight = r.FamilyWeight,
        MennoEnabled = r.MennoEnabled,
        MennoCompareMode = r.MennoCompareMode,
        MinSampleSize = r.MinSampleSize,
    };

    public static ReportRow ToRow(ReportDefinition d) => new() {
        Id = d.Id,
        AccountId = d.AccountId,
        Name = d.Name,
        Subject = d.Subject,
        Mode = d.Mode,
        DisplayMode = d.DisplayMode,
        GroupBy = d.GroupBy,
        SecondaryGroupBy = d.SecondaryGroupBy,
        TimeBucket = d.TimeBucket,
        CustomBucketN = d.CustomBucketN,
        CustomBucketUnit = d.CustomBucketUnit,
        Filters = SerializeFilters(d.Filters),
        GridX = d.GridX,
        GridY = d.GridY,
        GridW = d.GridW,
        GridH = d.GridH,
        Weight = string.IsNullOrEmpty(d.Weight) ? "LOW" : d.Weight,
        Color = string.IsNullOrEmpty(d.Color) ? "#6366f1" : d.Color,
        Description = d.Description,
        ChartType = string.IsNullOrEmpty(d.ChartType) ? "bar" : d.ChartType,
        SortOrder = d.SortOrder,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        ValueFilterOp = d.ValueFilterOp,
        ValueFilterThreshold = d.ValueFilterThreshold,
        GroupId = d.GroupId,
        NormalizeBy = string.IsNullOrEmpty(d.NormalizeBy) ? ReportDefaults.NormalizeNone : d.NormalizeBy,
        LabelColors = d.LabelColors,
        UnfilledColor = d.UnfilledColor,
        FamilyWeight = d.FamilyWeight,
        MennoEnabled = d.MennoEnabled,
        MennoCompareMode = string.IsNullOrEmpty(d.MennoCompareMode) ? "side_by_side" : d.MennoCompareMode,
        MinSampleSize = d.MinSampleSize,
    };

    public static ReportFilters ParseFilters(string? json, ILogger? logger = null) {
        if (string.IsNullOrWhiteSpace(json)) {
            return new ReportFilters();
        }
        try {
            return JsonSerializer.Deserialize<ReportFilters>(json, FilterOptions) ?? new ReportFilters();
        } catch (JsonException ex) {
            logger?.LogDebug(ex, "report filters JSON unreadable, using empty filters");
            return new ReportFilters();
        }
    }

    public static string SerializeFilters(ReportFilters filters) =>
        JsonSerializer.Serialize(filters, FilterOptions);
}
