namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineBar(
    string MissionId,
    int Lane,
    double LeftPercent,
    double WidthPercent,
    double FillPercent,
    bool IsActive,
    string ShipIconPath,
    string ShipName);
