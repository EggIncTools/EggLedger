using EggIdentity.DbClone;

namespace EggLedger.Web.Server.SubProd;

public static class LedgerClonePlan {
    public const string TargetDatabase = "eggledger_subprod";

    public static ClonePlan Plan { get; } = new("eggledger", TargetDatabase, [
        new TablePolicy("users", ClonePolicy.Scrub) {
            ScrubSql = "UPDATE users SET encryption_key = ''",
            VerifySql = "SELECT 1 FROM users WHERE encryption_key <> ''",
        },
        new TablePolicy("identities", ClonePolicy.Full),
        new TablePolicy("el_mission", ClonePolicy.Full),
        new TablePolicy("el_artifact_drops", ClonePolicy.Full),
        new TablePolicy("el_mission_fuel", ClonePolicy.Full),
        new TablePolicy("el_inflight_mission", ClonePolicy.Full),
        new TablePolicy("el_settings", ClonePolicy.Full),
        new TablePolicy("el_reports", ClonePolicy.Full),
        new TablePolicy("el_report_groups", ClonePolicy.Full),
        new TablePolicy("el_pinned_reports", ClonePolicy.Full),
        new TablePolicy("sessions", ClonePolicy.SchemaOnly),
        new TablePolicy("pending_auth", ClonePolicy.SchemaOnly),
        new TablePolicy("data_protection_keys", ClonePolicy.SchemaOnly),
        new TablePolicy("deploy_state", ClonePolicy.SchemaOnly),
        new TablePolicy("bot_channel_config", ClonePolicy.SchemaOnly),
        new TablePolicy("bot_channel_state", ClonePolicy.SchemaOnly),
        new TablePolicy("blobs", ClonePolicy.SchemaOnly),
        new TablePolicy("el_backup", ClonePolicy.SchemaOnly),
        new TablePolicy("el_api_spam", ClonePolicy.SchemaOnly),
        new TablePolicy("site_visits_daily", ClonePolicy.SchemaOnly),
        new TablePolicy("site_visit_paths_daily", ClonePolicy.SchemaOnly),
        new TablePolicy("app_settings", ClonePolicy.Skip),
        new TablePolicy("app_setting_collections", ClonePolicy.Skip),
    ]) {
        IgnoredTables = ["eggledger_migrations", "eggidentity_migrations", "eggidentity_settings_migrations", "eggidentity_visits_migrations", "subprod_stamp"],
    };

    public static EggIdentity.DbClone.SubProdFence Fence { get; } = new([
        new FenceGate("DISCORD", ["DISCORD_BOT_TOKEN", "DISCORD_CLIENT_ID"]),
        new FenceGate("DEPLOY", ["DEPLOY_AGENT_URL", "DEPLOY_AGENT_SECRET"]),
        new FenceGate("AUTH", ["IDENTITY_API_SECRET", "IDENTITY_API_URL"]),
        new FenceGate("MENNO", ["MENNO_FUNCTION_KEY"]),
    ]);
}
