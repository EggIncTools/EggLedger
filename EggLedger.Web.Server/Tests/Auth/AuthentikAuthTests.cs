using EggIdentity.Client;
using EggLedger.Web.Server.Auth;
using EggLedger.Web.Server.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EggLedger.Web.Server.Tests.Auth;

public class AuthentikAuthTests {
    private static AppConfig ConfigWith(string authority, string clientId = "", string clientSecret = "") => new(
        DatabaseUrl: "", DiscordClientId: "", PublicBaseUrl: "",
        BotToken: "", GuildId: "", SharedRoleId: "", DeployAgentUrl: "", DeployAgentSecret: "",
        DashboardChannelId: "", MennoFunctionKey: "",
        AuthentikAuthority: authority, AuthentikClientId: clientId, AuthentikClientSecret: clientSecret,
        IdentityApiUrl: "http://localhost:8090", IdentityApiSecret: "unused", IdentityWidgetUrl: "http://localhost:8090",
        TrustedProxyNetworks: [],
        BuildSha: "dev", BuildDate: "dev", DataProtectionCertPath: "", DataProtectionCertPassword: "");




    private static IdentityApiClient UnusedIdentityClient() =>
        new(new HttpClient { BaseAddress = new Uri("http://localhost:8090") });



    private static NpgsqlDataSource UnusedDataSource() =>
        NpgsqlDataSource.Create("Host=localhost;Username=unused;Password=unused;Database=unused");

    [Fact]
    public async Task AddIfConfigured_registers_oidc_scheme_when_authority_configured() {
        var services = new ServiceCollection();
        var builder = services.AddAuthentication(AuthScheme.Cookie).AddCookie(AuthScheme.Cookie);
        var registered = AuthentikAuth.AddIfConfigured(builder, ConfigWith(
            "https://auth.davidarthurcole.me/application/o/egg-ledger/", "abc", "secret"), UnusedIdentityClient(), UnusedDataSource());
        Assert.True(registered);

        var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.NotNull(scheme);
    }

    [Fact]
    public async Task AddIfConfigured_noop_when_authority_empty() {
        var services = new ServiceCollection();
        var builder = services.AddAuthentication(AuthScheme.Cookie).AddCookie(AuthScheme.Cookie);
        var registered = AuthentikAuth.AddIfConfigured(builder, ConfigWith(""), UnusedIdentityClient(), UnusedDataSource());
        Assert.False(registered);

        var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Null(scheme);
    }
}
