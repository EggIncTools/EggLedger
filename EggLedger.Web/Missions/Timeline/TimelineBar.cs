using EggLedger.Domain.MissionPacking;

namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineBar(
    string MissionId,
    int Lane,
    double LeftPercent,
    double WidthPercent,
    double FillPercent,
    bool IsActive,
    string ShipIconPath,
    string ShipName,
    int DurationIndex,
    string? TargetIconPath,
    DatabaseMission Mission,
    bool HasData = true,
    bool ShowBubble = true,
    bool ContinuesLeft = false,
    bool ContinuesRight = false);
