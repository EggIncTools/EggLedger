namespace EggLedger.Domain.MissionQuery;

public sealed class MissionDrop {
    public int Id { get; set; }
    public string SpecType { get; set; } = "";
    public string Name { get; set; } = "";
    public string GameName { get; set; } = "";
    public string EffectString { get; set; } = "";
    public int Level { get; set; }
    public int Rarity { get; set; }
    public double Quality { get; set; }
    public int IVOrder { get; set; }
}

public sealed class PossibleMission {
    public global::Ei.MissionInfo.Spaceship Ship { get; set; }
    public List<DurationConfig> Durations { get; set; } = [];
}

public sealed class DurationConfig {
    public global::Ei.MissionInfo.DurationType DurationType { get; set; }
    public double MinQuality { get; set; }
    public double MaxQuality { get; set; }
    public double LevelQualityBump { get; set; }
    public int MaxLevels { get; set; }
}

public sealed class DatabaseAccount {
    public string Id { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int MissionCount { get; set; }
    public string EBString { get; set; } = "";
    public string AccountColor { get; set; } = "";
    public double LastMissionReturnDT { get; set; }
}

public sealed class KnownAccount {
    public string Id { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string EBString { get; set; } = "";
    public string AccountColor { get; set; } = "";
}

public sealed class AccountInfo {
    public string Id { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string EBString { get; set; } = "";
    public string AccountColor { get; set; } = "";
    public string SeString { get; set; } = "";
    public int PeCount { get; set; }
    public int TeCount { get; set; }
    public double LastBackupTime { get; set; }

    public KnownAccount ToKnownAccount() => new() {
        Id = Id,
        Nickname = Nickname,
        EBString = EBString,
        AccountColor = AccountColor,
    };
}

public readonly record struct PlayerMissionStats(int Count, double MaxReturnTimestamp);
