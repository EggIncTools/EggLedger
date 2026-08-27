namespace EggLedger.Web.Services;

public enum AppState {
    AwaitingInput,
    FetchingSave,
    FetchingMissions,
    ExportingData,
    Success,
    Failed,
    Interrupted,
}

public enum SegmentStatus {
    Active,
    Done,
    Failed,
    Skipped,
}

public sealed record FetchProgress {
    public required AppState State { get; init; }
    public int Total { get; init; }

    public int Finished { get; init; }
    public int Failed { get; init; }

    public int Retried { get; init; }

    public string? MissionId { get; init; }

    public string? Segment { get; init; }

    public SegmentStatus? SegmentStatus { get; init; }

    public IReadOnlyList<FailedMission> FailedMissions { get; init; } = [];
}

public sealed record FailedMission(string MissionId, double StartTimestamp, string Reason = "") {
    public bool IsTimeout => Reason.Contains("timeout after", StringComparison.OrdinalIgnoreCase);
}
