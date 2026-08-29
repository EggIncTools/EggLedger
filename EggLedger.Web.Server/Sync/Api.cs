using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.Metrics;
using EggLedger.Web.Server.SubProd;
using EggLedger.Web.Server.Sync.Auth;
using EggLedger.Web.Server.Sync.Blobs;
using EggLedger.Web.Server.Sync.Db;
using EggLedger.Web.Server.Sync.Menno;
using EggLedger.Web.Server.Sync.Verify;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EggLedger.Web.Server.Sync;

public static class Api {
    public static void Map(WebApplication app, AppConfig cfg, VerifyInfo build) {
        var source = app.Services.GetService<NpgsqlDataSource>()
                     ?? NpgsqlDataSource.Create(cfg.DatabaseUrl);
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        var identity = app.Services.GetRequiredService<EggIdentity.Client.IdentityApiClient>();
        var currentUser = app.Services.GetRequiredService<EggLedger.Web.Server.Sync.Auth.ICurrentUser>();
        var auth = app.Services.GetRequiredService<AuthEndpoints>();
        var blobs = new BlobEndpoints(source, loggerFactory.CreateLogger<BlobEndpoints>());
        var menno = new MennoEndpoint(new HttpClient(), cfg.MennoFunctionKey, AppConfig.MennoUpstreamUrl);
        var store = new SessionStore(source, identity);
        var admin = new Admin.AdminEndpoints(app.Services.GetRequiredService<EggLedger.Web.Components.Admin.IAdminData>(), currentUser);

        app.UseEggIdentityRequestMetrics();


        app.MapGet("/api/v1/auth/pair/begin", (HttpContext c) => auth.PairBegin(c));
        if (!string.IsNullOrEmpty(cfg.AuthentikAuthority)) {
            app.MapGet("/api/v1/auth/authentik-login", (HttpContext c) =>
                Results.Challenge(
                    new AuthenticationProperties { RedirectUri = "/" },
                    [OpenIdConnectDefaults.AuthenticationScheme]));
        }
        app.MapGet("/api/v1/auth/poll", (HttpContext c) => auth.Poll(c, c.Request.Query["state"].ToString()));
        app.MapDelete("/api/v1/auth/session", (HttpContext c) => auth.DeleteSession(c));
        app.MapPost("/api/v1/auth/logout", (HttpContext c) => auth.Logout(c));




        app.MapPost("/api/v1/auth/session-from-login", (HttpContext c) => auth.SessionFromLogin(c));

        VerifyEndpoint.Map(app, build);
        if (!app.Environment.IsStaging() || SubProdFence.Allows("MENNO", Environment.GetEnvironmentVariable)) {
            app.MapPost("/api/v1/menno/submit", (HttpContext c) => menno.Submit(c));
        }


        MapAuthed(app, ["PUT"], "/api/v1/blobs/{name}", store,
            c => blobs.Put(c, (string)c.Request.RouteValues["name"]!));
        MapAuthed(app, ["GET"], "/api/v1/blobs/{name}", store,
            c => blobs.Get(c, (string)c.Request.RouteValues["name"]!));
        MapAuthed(app, ["GET"], "/api/v1/blobs", store,
            c => blobs.List(c));
        MapAuthed(app, ["DELETE"], "/api/v1/blobs/{name}", store,
            c => blobs.Delete(c, (string)c.Request.RouteValues["name"]!));
        MapAuthed(app, ["DELETE"], "/api/v1/user", store,
            c => blobs.DeleteUser(c));


        MapAuthed(app, ["GET"], "/api/v1/admin/me", store, admin.Me);
        MapAuthed(app, ["GET"], "/api/v1/admin/users", store, admin.Users);
        MapAuthed(app, ["DELETE"], "/api/v1/admin/users/{userId}", store,
            c => admin.DeleteUser(c, (string)c.Request.RouteValues["userId"]!));



        var ships = app.Services.GetRequiredService<Ships.ShipAssetService>();
        MapAuthed(app, ["GET"], "/api/ships/manifest", store, async ctx => {
            if (!await currentUser.IsAtLeastAsync(ctx, UserRole.Admin, ctx.RequestAborted)) {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsJsonAsync(
                ships.Manifest.Ships.ToDictionary(kv => kv.Key, kv => new { bbox = kv.Value.Bbox }),
                ctx.RequestAborted);
        });
        MapAuthed(app, ["GET"], "/api/ships/{key}.glb", store, async ctx => {
            bool isAdmin = await currentUser.IsAtLeastAsync(ctx, UserRole.Admin, ctx.RequestAborted);
            var key = (string)ctx.Request.RouteValues["key"]!;
            var r = await Ships.ShipEndpoints.HandleGlb(ships, isAdmin, key, ctx.RequestAborted);
            ctx.Response.StatusCode = r.Status;
            if (r.Bytes is not null) {
                ctx.Response.ContentType = r.ContentType!;
                ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                await ctx.Response.Body.WriteAsync(r.Bytes, ctx.RequestAborted);
            }
        });
    }

    private static void MapAuthed(WebApplication app, string[] methods, string pattern,
        ISessionStore store, RequestDelegate handler) {
        Task Pipeline(HttpContext ctx) => new RequireAuth(handler, store).Invoke(ctx);
        app.MapMethods(pattern, methods, (RequestDelegate)Pipeline);
    }
}
