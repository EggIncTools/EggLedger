using EggIdentity.Settings;
using EggLedger.Web.Server.Settings;

namespace EggLedger.Web.Server.Sync;

public sealed record AppConfig(
    string DatabaseUrl,
    string DiscordClientId,
    string PublicBaseUrl,
    string BotToken,
    string GuildId,
    string SharedRoleId,
    string DeployAgentUrl,
    string DeployAgentSecret,
    string DashboardChannelId,
    string MennoFunctionKey,
    string IdentityApiUrl,
    string IdentityApiSecret,
    string IdentityWidgetUrl,
    string AdminApiSecret,
    IReadOnlyList<string> TrustedProxyNetworks,
    string BuildSha,
    string BuildDate,
    string GitSha,
    string EgiBaseUrl,
    string EgiApiKey,
    string DataProtectionCertPath,
    string DataProtectionCertPassword,
    int SessionSweepIntervalMinutes) {
    public const string MennoUpstreamUrl = "https://eggincdatacollection.azurewebsites.net/api/SubmitEid";

    private static readonly string[] DefaultProxyNetworks =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7", "fe80::/10"];

    public static SettingsRegistry Registry { get; } =
        new([LedgerSettings.Provider, EggIdentity.Auth.SessionSettings.Provider, EggIdentity.DbClone.EnvironmentSettings.Provider]);

    public static AppConfig FromEnv(Func<string, string?> get) =>
        From(new SettingsSnapshot(Registry, new Dictionary<string, string?>(StringComparer.Ordinal), null, get));

    public static AppConfig From(ISettingsSource settings) {
        ArgumentNullException.ThrowIfNull(settings);
        string V(string key) => settings.Value(key).Value ?? string.Empty;

        var identityApiUrl = V(LedgerSettings.IdentityApiUrl);
        var proxyNets = settings.Value(LedgerSettings.TrustedProxyNetworks).AsList();

        return new AppConfig(
            DatabaseUrl: V(LedgerSettings.DatabaseUrl),
            DiscordClientId: V(LedgerSettings.DiscordClientId),
            PublicBaseUrl: V(LedgerSettings.PublicBaseUrl),
            BotToken: V(LedgerSettings.DiscordBotToken),
            GuildId: V(LedgerSettings.DiscordGuildId),
            SharedRoleId: V(LedgerSettings.SharedRoleId),
            DeployAgentUrl: V(LedgerSettings.DeployAgentUrl),
            DeployAgentSecret: V(LedgerSettings.DeployAgentSecret),
            DashboardChannelId: V(LedgerSettings.DashboardChannelId),
            MennoFunctionKey: V(LedgerSettings.MennoFunctionKey),
            IdentityApiUrl: identityApiUrl,
            IdentityApiSecret: V(LedgerSettings.IdentityApiSecret),
            IdentityWidgetUrl: settings.Value(LedgerSettings.IdentityWidgetUrl).Value is { Length: > 0 } widgetUrl
                ? widgetUrl
                : identityApiUrl,
            AdminApiSecret: V(LedgerSettings.AdminApiSecret),
            TrustedProxyNetworks: proxyNets.Count > 0 ? proxyNets : DefaultProxyNetworks,
            BuildSha: V(LedgerSettings.BuildSha),
            BuildDate: V(LedgerSettings.BuildDate),
            GitSha: V(LedgerSettings.GitSha),
            EgiBaseUrl: V(LedgerSettings.EgiBaseUrl),
            EgiApiKey: V(LedgerSettings.EgiApiKey),
            DataProtectionCertPath: V(LedgerSettings.DataProtectionCertPath),
            DataProtectionCertPassword: V(LedgerSettings.DataProtectionCertPassword),
            SessionSweepIntervalMinutes: Math.Max(1, settings.Value(LedgerSettings.SessionSweepIntervalMinutes).AsInt(10)));
    }
}
