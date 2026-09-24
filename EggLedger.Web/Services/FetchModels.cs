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

    public string? MissionLabel { get; init; }

    public string? LogText { get; init; }

    public bool LogIsError { get; init; }

    public bool LogRowOnly { get; init; }

    public IReadOnlyList<string> ExportedFiles { get; init; } = [];
}

public enum StageStatus {
    Pending,
    Active,
    Done,
    Failed,
    Skipped,
}

public enum ProcessStatus {
    Running,
    Done,
    Failed,
}

public sealed record FetchLogEntry(DateTimeOffset At, string Text, bool IsError);

public sealed record MissionSegment(string Name, SegmentStatus? Status);

public sealed class MissionProcess {
    public required string Id { get; init; }
    public required string Label { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public ProcessStatus Status { get; set; }
    public MissionSegment[] Segments { get; } = [new("Cache", null), new("Fetch", null), new("Decode", null), new("Store", null)];
    public List<FetchLogEntry> Logs { get; } = [];
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed record FailedMission(string MissionId, double StartTimestamp, string Reason = "") {
    public bool IsTimeout => Reason.Contains("timeout after", StringComparison.OrdinalIgnoreCase);
}
