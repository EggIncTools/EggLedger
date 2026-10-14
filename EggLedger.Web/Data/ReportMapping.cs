using System.Text.Json;
using EggIdentity.Reports;
using EggLedger.Domain.Reports;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public static class ReportMapping {
    public static LedgerReport ToReport(ReportRow r, ILogger? logger = null) {
        var (definition, placement) = Definition(r, logger);
        return new LedgerReport(definition, placement with { ReportId = r.Id, W = r.GridW > 0 ? r.GridW : placement.W, H = r.GridH > 0 ? r.GridH : placement.H, Order = r.SortOrder, Group = string.IsNullOrEmpty(r.GroupId) ? null : r.GroupId }) {
            AccountId = r.AccountId,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }

    public static ReportRow ToRow(LedgerReport report) {
        var d = report.Definition;
        var p = report.Placement;
        return new ReportRow {
            Id = d.Id,
            AccountId = report.AccountId,
            Name = d.Title,
            Subject = d.Query.Source,
            Description = d.Description ?? "",
            ChartType = d.View.Chart,
            Color = d.View.Color ?? "",
            GridX = p.X,
            GridY = p.Y,
            GridW = p.W,
            GridH = p.H,
            SortOrder = p.Order,
            GroupId = p.Group ?? "",
            CreatedAt = report.CreatedAt,
            UpdatedAt = report.UpdatedAt,
            Definition = ReportCodec.Serialize(d),
        };
    }

    public static ReportRow WithPlacement(ReportRow row, ReportPlacement placement) => row with {
        GridX = placement.X,
        GridY = placement.Y,
        GridW = placement.W,
        GridH = placement.H,
        SortOrder = placement.Order,
        GroupId = placement.Group ?? "",
    };

    private static (ReportDefinition Definition, ReportPlacement Placement) Definition(ReportRow r, ILogger? logger) {
        if (!string.IsNullOrWhiteSpace(r.Definition)) {
            try {
                var (definition, _) = ReportCodec.Upgrade(r.Definition, LegacyReportUpgrade.Upgrades);
                return (definition, new ReportPlacement(r.Id, r.GridX, r.GridY, r.GridW, r.GridH, r.SortOrder));
            } catch (JsonException ex) {
                logger?.LogDebug(ex, "report definition JSON unreadable, falling back to legacy columns");
            }
        }
        return LegacyReportUpgrade.Convert(Legacy(r, logger));
    }

    private static LegacyReportDefinition Legacy(ReportRow r, ILogger? logger) => new() {
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

    public static FilterSet ParseFilters(string? json, ILogger? logger = null) {
        try {
            return FilterCodec.Deserialize(json);
        } catch (JsonException ex) {
            logger?.LogDebug(ex, "report filters JSON unreadable, using empty filters");
            return FilterSet.Empty;
        }
    }
}
