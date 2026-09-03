using EggIdentity.Settings;
using EggLedger.Web.Server.Settings;
using EggLedger.Web.Server.Sync;
using Xunit;

namespace EggLedger.Web.Server.Tests.Settings;

public class LedgerSettingsTests {
    private static SettingsSnapshot Snapshot(
        Dictionary<string, string?>? database = null, Dictionary<string, string?>? env = null) =>
        new(AppConfig.Registry,
            database ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            null,
            k => env?.GetValueOrDefault(k));

    [Fact]
    public void Registry_composes_ledger_and_session_providers() {
        var keys = AppConfig.Registry.All.Select(d => d.Key).ToList();
        Assert.Contains(LedgerSettings.DatabaseUrl, keys);
        Assert.Contains("session.secret", keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Only_public_base_url_and_widget_url_apply_live() {
        var live = LedgerSettings.Provider.Describe()
            .Where(d => d.Tier == ApplyTier.Live)
            .Select(d => d.Key)
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            new[] { LedgerSettings.PublicBaseUrl, LedgerSettings.IdentityWidgetUrl }.Order(StringComparer.Ordinal),
            live);
    }

    [Fact]
    public void Bootstrap_tier_matches_the_documented_set() {
        var bootstrap = LedgerSettings.Provider.Describe()
            .Where(d => d.Tier == ApplyTier.Bootstrap)
            .Select(d => d.EnvKey)
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            new[] {
                "ASPNETCORE_URLS", "BUILD_DATE", "BUILD_SHA", "DATABASE_URL",
                "DATA_PROTECTION_CERT_PASSWORD", "DATA_PROTECTION_CERT_PATH",
                "GIT_SHA", "IDENTITY_API_SECRET", "IDENTITY_API_URL",
            }.Order(StringComparer.Ordinal),
            bootstrap);
    }

    [Fact]
    public void Database_wins_over_environment_for_a_restart_tier_setting() {
        var cfg = AppConfig.From(Snapshot(
            database: new Dictionary<string, string?>(StringComparer.Ordinal) {
                [LedgerSettings.AuthentikClientId] = "from-db",
            },
            env: new Dictionary<string, string?>(StringComparer.Ordinal) {
                ["AUTHENTIK_CLIENT_ID"] = "from-env",
            }));
        Assert.Equal("from-db", cfg.AuthentikClientId);
    }

    [Fact]
    public void Database_is_ignored_for_a_bootstrap_setting() {
        var cfg = AppConfig.From(Snapshot(
            database: new Dictionary<string, string?>(StringComparer.Ordinal) {
                [LedgerSettings.DatabaseUrl] = "Host=from-db",
            },
            env: new Dictionary<string, string?>(StringComparer.Ordinal) {
                ["DATABASE_URL"] = "Host=from-env",
            }));
        Assert.Equal("Host=from-env", cfg.DatabaseUrl);
    }

    [Fact]
    public void Public_base_url_falls_back_to_its_descriptor_default() {
        var cfg = AppConfig.From(Snapshot());
        Assert.Equal(LedgerSettings.DefaultPublicBaseUrl, cfg.PublicBaseUrl);
    }

    [Fact]
    public void Trusted_proxy_networks_falls_back_to_private_ranges() {
        var cfg = AppConfig.From(Snapshot());
        Assert.Contains("127.0.0.0/8", cfg.TrustedProxyNetworks);
        Assert.Contains("192.168.0.0/16", cfg.TrustedProxyNetworks);
    }

    [Fact]
    public void Trusted_proxy_networks_reads_a_comma_separated_list() {
        var cfg = AppConfig.From(Snapshot(
            env: new Dictionary<string, string?>(StringComparer.Ordinal) {
                ["TRUSTED_PROXY_NETWORKS"] = "10.1.0.0/16, 10.2.0.0/16",
            }));
        Assert.Equal(["10.1.0.0/16", "10.2.0.0/16"], cfg.TrustedProxyNetworks);
    }

    [Fact]
    public void Build_stamps_default_to_dev() {
        var cfg = AppConfig.From(Snapshot());
        Assert.Equal("dev", cfg.BuildSha);
        Assert.Equal("dev", cfg.BuildDate);
    }

    [Fact]
    public void Secrets_are_masked_for_display() {
        var snapshot = Snapshot(env: new Dictionary<string, string?>(StringComparer.Ordinal) {
            ["DISCORD_BOT_TOKEN"] = "super-secret",
        });
        Assert.Equal("********", snapshot.Value(LedgerSettings.DiscordBotToken).Display);
    }
}
