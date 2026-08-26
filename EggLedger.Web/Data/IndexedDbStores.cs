namespace EggLedger.Web.Data;

public static class IndexedDbStores {
    public const string Mission = "mission";
    public const string InFlightMission = "inflight_mission";
    public const string Backup = "backup";
    public const string ArtifactDrops = "artifact_drops";
    public const string Settings = "settings";
    public const string Reports = "reports";
    public const string ReportGroups = "report_groups";
    public const string PlayerIdIndex = "player_id";
    public const string AccountIdIndex = "account_id";

    private static readonly HashSet<string> Indexes = [PlayerIdIndex, AccountIdIndex];

    public static string ValidIndex(string index) =>
        Indexes.Contains(index) ? index
            : throw new ArgumentException($"unknown index '{index}'", nameof(index));
}
