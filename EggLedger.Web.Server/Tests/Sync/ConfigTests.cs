using EggLedger.Web.Server.Sync;

namespace EggLedger.Web.Server.Tests.Sync;

public class ConfigTests {
    [Fact]
    public void FromEnv_reads_identity_api_settings() {
        var env = new Dictionary<string, string?> {
            ["IDENTITY_API_URL"] = "http://localhost:8090",
            ["IDENTITY_API_SECRET"] = "shh",
        };
        var cfg = AppConfig.FromEnv(k => env.GetValueOrDefault(k));
        Assert.Equal("http://localhost:8090", cfg.IdentityApiUrl);
        Assert.Equal("shh", cfg.IdentityApiSecret);
    }

    [Fact]
    public void FromEnv_defaults_identity_api_settings_to_empty() {
        var cfg = AppConfig.FromEnv(_ => null);
        Assert.Equal("", cfg.IdentityApiUrl);
        Assert.Equal("", cfg.IdentityApiSecret);
    }

    [Fact]
    public void FromEnv_reads_identity_widget_url_when_set() {
        var env = new Dictionary<string, string?> {
            ["IDENTITY_API_URL"] = "http://eggidentity:8090",
            ["IDENTITY_WIDGET_URL"] = "https://identity.egginc.tools",
        };
        var cfg = AppConfig.FromEnv(k => env.GetValueOrDefault(k));
        Assert.Equal("https://identity.egginc.tools", cfg.IdentityWidgetUrl);
    }

    [Fact]
    public void FromEnv_defaults_identity_widget_url_to_identity_api_url() {
        var env = new Dictionary<string, string?> {
            ["IDENTITY_API_URL"] = "http://eggidentity:8090",
        };
        var cfg = AppConfig.FromEnv(k => env.GetValueOrDefault(k));
        Assert.Equal("http://eggidentity:8090", cfg.IdentityWidgetUrl);
    }
}
