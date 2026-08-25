using EggIdentity.Auth;
using EggIdentity.Bot;
using EggIdentity.Contract;
using EggIdentity.Db;
using EggIdentity.Metrics;
using EggIdentity.Metrics.AdminUi;
using EggLedger.Web;
using EggLedger.Web.Data;
using EggLedger.Web.Server;
using EggLedger.Web.Server.Components;
using EggLedger.Web.Server.SubProd;
using EggLedger.Web.Server.Sync;
using EggLedger.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

SubProdFence.ForceSessionIsolation(Environment.SetEnvironmentVariable, builder.Environment.EnvironmentName);
var fencedGetter = SubProdFence.WrapGetter(Environment.GetEnvironmentVariable, builder.Environment.EnvironmentName, out var subProdFenceReport);
foreach (var entry in subProdFenceReport) {
    if (entry.Forced) {
        Console.Error.WriteLine($"eggledger: sub-prod fence forced {entry.Key} empty");
    }
}

var cfg = AppConfig.FromEnv(fencedGetter);
var hasDb = !string.IsNullOrEmpty(cfg.DatabaseUrl);
if (hasDb && builder.Environment.IsStaging()) {
    SubProdBootGuard.EnsureSubProdDatabase(cfg.DatabaseUrl);
}
var build = new VerifyInfo { Name = "EggLedger", Sha256 = cfg.BuildSha, Version = EggLedger.Web.AppVersionInfo.Current, Date = cfg.BuildDate };
var startedAt = DateTimeOffset.UtcNow;



builder.Services.AddSingleton(cfg);
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = builder.Environment.IsDevelopment());




builder.Services.Configure<ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = null;
    foreach (var cidr in cfg.TrustedProxyNetworks) {
        if (System.Net.IPNetwork.TryParse(cidr, out var net)) {
            o.KnownIPNetworks.Add(net);
        }
    }
});



var eggIdentitySession = SessionCookieOptions.FromEnvironment();
const string SmartAuthScheme = "smart";

var authentication = eggIdentitySession is null
    ? builder.Services.AddAuthentication(EggLedger.Web.Server.Auth.AuthScheme.Cookie)
    : builder.Services.AddAuthentication(SmartAuthScheme)
        .AddPolicyScheme(SmartAuthScheme, SmartAuthScheme, o => {
            o.ForwardDefaultSelector = ctx =>
                ctx.Request.Cookies.ContainsKey(eggIdentitySession.CookieName)
                    ? EggIdentitySessionDefaults.Scheme
                    : EggLedger.Web.Server.Auth.AuthScheme.Cookie;
        });

var authBuilder = authentication
    .AddCookie(EggLedger.Web.Server.Auth.AuthScheme.Cookie, o => {
        o.Cookie.Name = "el_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;




        o.Events.OnValidatePrincipal = async ctx => {
            var claim = ctx.Principal?.FindFirst(EggLedger.Web.Server.Auth.AuthScheme.UserIdClaim)?.Value;
            if (!Guid.TryParse(claim, out _)) {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(EggLedger.Web.Server.Auth.AuthScheme.Cookie);
                return;
            }
            var identity = ctx.HttpContext.RequestServices.GetService<EggIdentity.Client.IdentityApiClient>();
            if (identity is not null) {
                await EggIdentity.Auth.AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, EggLedger.Web.Server.Auth.AuthScheme.UserIdClaim, EggLedger.Web.Server.Auth.AuthScheme.RoleClaim);
            }
        };
    });

if (eggIdentitySession is not null) {
    authBuilder.AddEggIdentitySession(eggIdentitySession, onValidated: (principal, _, _) => {
        var userId = principal.EggIdentityUserId();
        if (userId is not null && principal.Identity is System.Security.Claims.ClaimsIdentity id) {
            id.AddClaim(new System.Security.Claims.Claim(EggLedger.Web.Server.Auth.AuthScheme.UserIdClaim, userId.Value.ToString()));
        }
        return Task.CompletedTask;
    });
    builder.Services.AddSingleton(eggIdentitySession);
    builder.Services.AddScoped<EggLedger.Web.Components.IProfilePanelSlot, EggLedger.Web.Server.Components.ProfilePanelSlot>();
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<EggLedger.Web.Components.Admin.IBotConfigSlot, EggLedger.Web.Server.Bot.BotConfigSlot>();

if (hasDb) {
    var dataSource = NpgsqlDataSource.Create(cfg.DatabaseUrl);
    builder.Services.AddSingleton(dataSource);



    var dp = builder.Services.AddDataProtection()
        .SetApplicationName("EggLedger")
        .AddKeyManagementOptions(o =>
            o.XmlRepository = new EggLedger.Web.Server.Auth.PostgresXmlRepository(dataSource));




    if (!string.IsNullOrEmpty(cfg.DataProtectionCertPath)) {
        try {
            var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                cfg.DataProtectionCertPath, cfg.DataProtectionCertPassword);
            dp.ProtectKeysWithCertificate(cert);
        } catch (Exception ex) {
            Console.Error.WriteLine($"eggledger: WARNING - failed to load DataProtection cert from {cfg.DataProtectionCertPath}: {ex.Message}. DataProtection keyring is stored unencrypted in Postgres.");
        }
    } else {
        Console.Error.WriteLine("eggledger: WARNING - DATA_PROTECTION_CERT_PATH not set. DataProtection keyring is stored unencrypted in Postgres.");
    }





    var identityHttp = new HttpClient { BaseAddress = new Uri(cfg.IdentityApiUrl) };
    identityHttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.IdentityApiSecret);
    var identityClient = new EggIdentity.Client.IdentityApiClient(identityHttp);
    builder.Services.AddSingleton(identityClient);
    builder.Services.AddSingleton<EggLedger.Web.Server.Sync.Auth.ICurrentUser, EggLedger.Web.Server.Sync.Auth.CurrentUser>();


    builder.Services.AddSingleton<EggLedger.Web.Server.Sync.Auth.AuthEndpoints>();
    builder.Services.AddScoped<EggLedger.Web.Components.Auth.IPairCompletion, EggLedger.Web.Server.Auth.PairCompletion>();
    builder.Services.AddScoped<EggLedger.Web.Components.Admin.IAdminAccess, EggLedger.Web.Server.Auth.AdminAccess>();

    builder.Services.AddEggIdentityRequestMetrics(o => o.PathPrefix = "/api/v1");
    builder.Services.AddSingleton<EggLedger.Web.Server.Sync.Admin.SpamLog>();
    builder.Services.AddSingleton<IRequestAuditSink>(sp => sp.GetRequiredService<EggLedger.Web.Server.Sync.Admin.SpamLog>());
    builder.Services.AddSingleton<ITrafficSource, EggLedger.Web.Server.Sync.Admin.InProcessTrafficSource>();
    builder.Services.AddScoped<EggLedger.Web.Components.Admin.ITrafficPanelSlot, EggLedger.Web.Server.Sync.Admin.TrafficPanelSlot>();
    builder.Services.AddSingleton<EggLedger.Web.Components.Admin.IAdminData, EggLedger.Web.Server.Sync.Admin.AdminDataService>();

    EggLedger.Web.Server.Auth.AuthentikAuth.AddIfConfigured(authBuilder, cfg, identityClient, dataSource);

    if (!string.IsNullOrEmpty(cfg.BotToken)) {
        builder.Services.AddSingleton(new BotConfig {
            Name = "EggLedger",
            Token = cfg.BotToken,
            AppId = cfg.DiscordClientId,
            GuildId = cfg.GuildId,
            RepoUrl = "https://github.com/DavidArthurCole/EggLedger",
            Build = build,
            DeployAgentUrl = cfg.DeployAgentUrl,
            DeployAgentSecret = cfg.DeployAgentSecret,
            SharedRoleId = cfg.SharedRoleId,
            DashboardChannelId = cfg.DashboardChannelId,
            PostgresConnectionString = cfg.DatabaseUrl,
            DashboardProvider = _ => Task.FromResult(new EggIdentity.Contract.DashboardSnapshot {
                AppName = "EggLedger",
                Version = build.Version,
                BuildHash = build.Sha256,
                DeployStatus = "online",
                UptimeSince = startedAt,
                RepoUrl = "https://github.com/DavidArthurCole/EggLedger",
            }),
        });
        builder.Services.AddSingleton<EggLedger.Web.Server.Bot.EggLedgerBotHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EggLedger.Web.Server.Bot.EggLedgerBotHostedService>());
        builder.Services.AddScoped(sp => sp.GetRequiredService<EggLedger.Web.Server.Bot.EggLedgerBotHostedService>().Bot?.ConfigService!);
    }
}



var selfBase = new Uri(builder.Configuration["SelfBaseAddress"] ?? SelfBaseFromUrls());
builder.Services.AddEggLedgerWeb(selfBase);
builder.Services.AddScoped<EggLedger.Web.Platform.IUserTimeZoneProvider, EggLedger.Web.Server.Platform.BrowserTimeZoneProvider>();

builder.Services.AddScoped(sp => {
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    var handler = new CookieForwardingHandler(accessor) { InnerHandler = new HttpClientHandler() };
    return new HttpClient(handler) { BaseAddress = selfBase };
});

static string SelfBaseFromUrls() {
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    var first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    if (string.IsNullOrEmpty(first)) {
        return "http://localhost:5015";
    }
    return first.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost").Replace("+", "localhost");
}



builder.Services.AddScoped<EggLedger.Web.Server.Storage.CurrentUser>();
if (hasDb) {
    builder.Services.AddScoped<ISessionStore>(sp =>
        new EggLedger.Web.Server.Sync.Db.SessionStore(
            sp.GetRequiredService<NpgsqlDataSource>(),
            sp.GetRequiredService<EggIdentity.Client.IdentityApiClient>()));
    builder.Services.RemoveAll<IIndexedDb>();
    builder.Services.AddScoped<IIndexedDb>(sp => new EggLedger.Web.Server.Storage.PostgresIndexedDb(
        sp.GetRequiredService<NpgsqlDataSource>(),
        sp.GetRequiredService<EggLedger.Web.Server.Storage.CurrentUser>()));
    builder.Services.AddScoped<EggLedger.Web.Server.Storage.FirstLoginBackfill>();
}



builder.Services.AddSingleton(_ =>
    new EggLedger.Web.Server.Ships.ShipAssetService(
        Path.Combine(builder.Environment.ContentRootPath, "ships")));


builder.Services.AddHttpClient("auxbrain", c => c.BaseAddress = new Uri("https://www.auxbrain.com"));

var app = builder.Build();

app.UseForwardedHeaders();


app.UseRouting();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
if (hasDb) {


    app.UseMiddleware<EggLedger.Web.Server.Sync.Auth.LoginCallbackMiddleware>();
}


app.UseAntiforgery();


app.Map("/egg-api/{**rest}", async (HttpContext ctx, IHttpClientFactory factory, string rest) => {
    var client = factory.CreateClient("auxbrain");
    using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), "/" + rest + ctx.Request.QueryString);
    if (ctx.Request.ContentLength is > 0) {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms);
        req.Content = new ByteArrayContent(ms.ToArray());
        if (ctx.Request.ContentType is { } ct) {
            req.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
        }
    }
    using var upstream = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
    ctx.Response.StatusCode = (int)upstream.StatusCode;
    foreach (var h in upstream.Content.Headers) {
        ctx.Response.Headers[h.Key] = h.Value.ToArray();
    }
    await upstream.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
});


if (hasDb) {


    app.Logger.LogInformation("eggledger: DB configured, running migrations. selfBase={SelfBase}", selfBase);
    var conn = await Database.InitAsync(cfg.DatabaseUrl);
    await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
    app.Logger.LogInformation("eggledger: migrations complete, /api/v1 + Postgres storage active");

    Api.Map(app, cfg, build);

    if (!string.IsNullOrEmpty(cfg.BotToken)) {
        var deployDataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
        var deployConfigStore = new ChannelConfigStore(deployDataSource);
        var botCfg = app.Services.GetRequiredService<BotConfig>();
        var botHosted = app.Services.GetRequiredService<EggLedger.Web.Server.Bot.EggLedgerBotHostedService>();
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () => {
            var client = botHosted.Bot?.Client;
            if (client is null || !ulong.TryParse(cfg.GuildId, out var guildId)) return;
            var notifier = new DeployNotifier(deployConfigStore, client, guildId, botCfg.Name);
            var tracker = new DeployVersionTracker(new DeployStateStore(deployDataSource), notifier);
            try {
                await tracker.CheckAndNotifyAsync(
                    botCfg.Name, Environment.GetEnvironmentVariable("GIT_SHA") ?? "", botCfg.Build.Version, CancellationToken.None);
            } catch (Exception ex) {
                app.Logger.LogWarning(ex, "eggledger: deploy self-report failed, continuing");
            }
        }));
    }
} else {
    app.Logger.LogWarning("eggledger: WARNING - DATABASE_URL not set. /api/v1 + Postgres storage DISABLED; auth/sync will not work. UI boots only.");
}

app.MapRazorComponents<AppHost>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(App).Assembly);

app.Run();
