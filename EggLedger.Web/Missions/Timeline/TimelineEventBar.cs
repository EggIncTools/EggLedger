namespace EggLedger.Web.Missions.Timeline;

public sealed record TimelineEventBar(
    string Id,
    string Type,
    string? Message,
    double Multiplier,
    bool Ultra,
    long StartUnix,
    long EndUnix,
    double LeftPercent,
    double WidthPercent,
    bool ContinuesLeft,
    bool ContinuesRight);
