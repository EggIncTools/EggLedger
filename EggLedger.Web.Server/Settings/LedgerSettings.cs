using EggIdentity.Settings;

namespace EggLedger.Web.Server.Settings;

public static class LedgerSettings {
    private const string Deploy = "Deploy and Ops";
    private const string Discord = "Discord";
    private const string Identity = "Identity and SSO";
    private const string Integrations = "Integrations";
    private const string Build = "Build";

    public const string DatabaseUrl = "deploy.database_url";
    public const string AspNetCoreUrls = "deploy.aspnetcore_urls";
    public const string PublicBaseUrl = "deploy.public_base_url";
    public const string TrustedProxyNetworks = "deploy.trusted_proxy_networks";
    public const string DeployAgentUrl = "deploy.agent_url";
    public const string DeployAgentSecret = "deploy.agent_secret";
    public const string DataProtectionCertPath = "deploy.data_protection_cert_path";
    public const string DataProtectionCertPassword = "deploy.data_protection_cert_password";

    public const string DiscordClientId = "discord.client_id";
    public const string DiscordBotToken = "discord.bot_token";
    public const string DiscordGuildId = "discord.guild_id";
    public const string SharedRoleId = "discord.shared_role_id";
    public const string DashboardChannelId = "discord.dashboard_channel_id";

    public const string AuthentikAuthority = "authentik.authority";
    public const string AuthentikClientId = "authentik.client_id";
    public const string AuthentikClientSecret = "authentik.client_secret";
    public const string IdentityApiUrl = "identity.api_url";
    public const string IdentityApiSecret = "identity.api_secret";
    public const string IdentityWidgetUrl = "identity.widget_url";

    public const string MennoFunctionKey = "integrations.menno_function_key";
    public const string EgiBaseUrl = "integrations.egi_base_url";
    public const string EgiApiKey = "integrations.egi_api_key";

    public const string BuildSha = "build.build_sha";
    public const string BuildDate = "build.build_date";
    public const string GitSha = "build.git_sha";

    public const string DefaultPublicBaseUrl = "https://eggledger.davidarthurcole.me";

    public static ISettingsProvider Provider { get; } = new StaticSettingsProvider([
        new SettingDescriptor(
            DatabaseUrl, "DATABASE_URL", "Database URL", Deploy,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret) {
            Description = "Read before the settings store exists, so it can never move into the database.",
        },
        new SettingDescriptor(
            AspNetCoreUrls, "ASPNETCORE_URLS", "Listen URLs", Deploy,
            SettingKind.Text, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Bound by the host before any application code runs.",
        },
        new SettingDescriptor(
            PublicBaseUrl, "PUBLIC_BASE_URL", "Public base URL", Deploy,
            SettingKind.Url, ApplyTier.Live, Sensitivity.Plain) {
            Description = "Read per request, so a change applies without a restart.",
            Default = DefaultPublicBaseUrl,
        },
        new SettingDescriptor(
            TrustedProxyNetworks, "TRUSTED_PROXY_NETWORKS", "Trusted proxy networks", Deploy,
            SettingKind.CidrList, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Falls back to the loopback and private ranges when unset.",
        },
        new SettingDescriptor(
            DeployAgentUrl, "DEPLOY_AGENT_URL", "Deploy agent URL", Deploy,
            SettingKind.Url, ApplyTier.RestartRequired, Sensitivity.Plain),
        new SettingDescriptor(
            DeployAgentSecret, "DEPLOY_AGENT_SECRET", "Deploy agent secret", Deploy,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret),
        new SettingDescriptor(
            DataProtectionCertPath, "DATA_PROTECTION_CERT_PATH", "Data protection cert path", Deploy,
            SettingKind.Path, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Without it the DataProtection keyring is stored unencrypted in Postgres.",
        },
        new SettingDescriptor(
            DataProtectionCertPassword, "DATA_PROTECTION_CERT_PASSWORD", "Data protection cert password", Deploy,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret),
        new SettingDescriptor(
            DiscordClientId, "DISCORD_CLIENT_ID", "Discord client id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new SettingDescriptor(
            DiscordBotToken, "DISCORD_BOT_TOKEN", "Discord bot token", Discord,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret) {
            Description = "Presence of this value is what enables the bot at startup.",
        },
        new SettingDescriptor(
            DiscordGuildId, "DISCORD_GUILD_ID", "Discord guild id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new SettingDescriptor(
            SharedRoleId, "SHARED_ROLE_ID", "Shared role id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new SettingDescriptor(
            DashboardChannelId, "DISCORD_DASHBOARD_CHANNEL_ID", "Dashboard channel id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new SettingDescriptor(
            AuthentikAuthority, "AUTHENTIK_AUTHORITY", "Authentik authority", Identity,
            SettingKind.Url, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Empty disables Authentik sign-in entirely.",
        },
        new SettingDescriptor(
            AuthentikClientId, "AUTHENTIK_CLIENT_ID", "Authentik client id", Identity,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new SettingDescriptor(
            AuthentikClientSecret, "AUTHENTIK_CLIENT_SECRET", "Authentik client secret", Identity,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret),
        new SettingDescriptor(
            IdentityApiUrl, "IDENTITY_API_URL", "Identity API URL", Identity,
            SettingKind.Url, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "The identity client is built from it before authentication is wired up.",
            Required = true,
        },
        new SettingDescriptor(
            IdentityApiSecret, "IDENTITY_API_SECRET", "Identity API secret", Identity,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret) { Required = true },
        new SettingDescriptor(
            IdentityWidgetUrl, "IDENTITY_WIDGET_URL", "Identity widget URL", Identity,
            SettingKind.Url, ApplyTier.Live, Sensitivity.Plain) {
            Description = "Read per request, and falls back to the identity API URL when unset.",
        },
        new SettingDescriptor(
            MennoFunctionKey, "MENNO_FUNCTION_KEY", "Menno function key", Integrations,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret),
        new SettingDescriptor(
            EgiBaseUrl, "EGI_BASE_URL", "EGI base URL", Integrations,
            SettingKind.Url, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Unset leaves the game events service inert.",
        },
        new SettingDescriptor(
            EgiApiKey, "EGI_API_KEY", "EGI API key", Integrations,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret),
        new SettingDescriptor(
            BuildSha, "BUILD_SHA", "Build SHA", Build,
            SettingKind.ReadOnly, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Stamped into the image at build time.",
            Default = "dev",
        },
        new SettingDescriptor(
            BuildDate, "BUILD_DATE", "Build date", Build,
            SettingKind.ReadOnly, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Stamped into the image at build time.",
            Default = "dev",
        },
        new SettingDescriptor(
            GitSha, "GIT_SHA", "Build commit", Build,
            SettingKind.ReadOnly, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Stamped into the image at build time.",
        },
    ]);
}
