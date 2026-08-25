using EggLedger.Web.Server.SubProd;
using Xunit;

namespace EggLedger.Web.Server.Tests.SubProd;

public class SubProdFenceTests {
    private static Func<string, string?> Env(params (string Key, string Value)[] pairs) {
        var dict = pairs.ToDictionary(p => p.Key, p => p.Value);
        return key => dict.TryGetValue(key, out var v) ? v : null;
    }

    [Fact]
    public void NonStaging_PassesEverythingThrough() {
        var inner = Env(("DISCORD_BOT_TOKEN", "real-token"), ("IDENTITY_API_URL", "https://identity.real"));
        var wrapped = SubProdFence.WrapGetter(inner, "Production", out var report);

        Assert.Equal("real-token", wrapped("DISCORD_BOT_TOKEN"));
        Assert.Equal("https://identity.real", wrapped("IDENTITY_API_URL"));
        Assert.Empty(report);
    }

    [Theory]
    [InlineData("DISCORD_BOT_TOKEN")]
    [InlineData("DISCORD_CLIENT_ID")]
    [InlineData("DEPLOY_AGENT_URL")]
    [InlineData("DEPLOY_AGENT_SECRET")]
    [InlineData("AUTHENTIK_CLIENT_SECRET")]
    [InlineData("IDENTITY_API_SECRET")]
    public void Staging_WithoutAllow_BlanksForcedKey(string key) {
        var inner = Env((key, "real-value"));
        var wrapped = SubProdFence.WrapGetter(inner, "Staging", out var report);

        Assert.Equal("", wrapped(key));
        Assert.Contains(report, e => e.Key == key && e.Forced);
    }

    [Fact]
    public void Staging_WithoutAuthAllow_RedirectsIdentityApiUrlToLoopback() {
        var inner = Env(("IDENTITY_API_URL", "https://identity.real"));
        var wrapped = SubProdFence.WrapGetter(inner, "Staging", out var report);

        Assert.Equal("http://127.0.0.1:1", wrapped("IDENTITY_API_URL"));
        Assert.Contains(report, e => e.Key == "IDENTITY_API_URL" && e.Forced);
    }

    [Fact]
    public void Staging_WithDiscordAllowTrue_LeavesDiscordKeysIntact() {
        var inner = Env(("SUBPROD_ALLOW_DISCORD", "true"), ("DISCORD_BOT_TOKEN", "real-token"), ("DISCORD_CLIENT_ID", "real-id"));
        var wrapped = SubProdFence.WrapGetter(inner, "Staging", out var report);

        Assert.Equal("real-token", wrapped("DISCORD_BOT_TOKEN"));
        Assert.Equal("real-id", wrapped("DISCORD_CLIENT_ID"));
        Assert.DoesNotContain(report, e => e.Key == "DISCORD_BOT_TOKEN" && e.Forced);
    }

    [Fact]
    public void Staging_WithAuthAllowTrue_LeavesIdentityApiUrlAndSecretIntact() {
        var inner = Env(("SUBPROD_ALLOW_AUTH", "true"), ("IDENTITY_API_URL", "https://identity.real"), ("IDENTITY_API_SECRET", "real-secret"));
        var wrapped = SubProdFence.WrapGetter(inner, "Staging", out var report);

        Assert.Equal("https://identity.real", wrapped("IDENTITY_API_URL"));
        Assert.Equal("real-secret", wrapped("IDENTITY_API_SECRET"));
    }

    [Fact]
    public void Allows_IsCaseInsensitiveOnTrue() {
        var inner = Env(("SUBPROD_ALLOW_MENNO", "TRUE"));
        Assert.True(SubProdFence.Allows("MENNO", inner));
    }

    [Fact]
    public void Allows_FalseWhenAbsent() {
        Assert.False(SubProdFence.Allows("MENNO", _ => null));
    }

    [Fact]
    public void ForceSessionIsolation_NonStaging_DoesNothing() {
        var calls = new List<(string, string?)>();
        SubProdFence.ForceSessionIsolation((k, v) => calls.Add((k, v)), "Production");
        Assert.Empty(calls);
    }

    [Fact]
    public void ForceSessionIsolation_Staging_SetsAllThreeKeysInRealEnvironmentChannel() {
        var calls = new Dictionary<string, string?>();
        SubProdFence.ForceSessionIsolation((k, v) => calls[k] = v, "Staging");

        Assert.True(calls.ContainsKey("EGGIDENTITY_SESSION_SECRET"));
        Assert.False(string.IsNullOrEmpty(calls["EGGIDENTITY_SESSION_SECRET"]));
        Assert.True(calls.ContainsKey("EGGIDENTITY_SESSION_SECRET_PREVIOUS"));
        Assert.False(string.IsNullOrEmpty(calls["EGGIDENTITY_SESSION_SECRET_PREVIOUS"]));
        Assert.True(calls.ContainsKey("EGGIDENTITY_SESSION_COOKIE_DOMAIN"));
        Assert.Null(calls["EGGIDENTITY_SESSION_COOKIE_DOMAIN"]);
    }
}
