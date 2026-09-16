using System.Net;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggLedger.Web.Server.Sync.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EggLedger.Web.Server.Tests.Sync.Auth;

public class CurrentUserTests {
    private static HttpContext ContextWithUserId(string? userId) {
        var ctx = new DefaultHttpContext();
        if (userId is not null) ctx.Request.Headers[RequireAuth.UserIdHeader] = userId;
        return ctx;
    }

    private static IdentityApiClient ClientReturningRole(string role) {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            $$"""{"userId":"11111111-1111-1111-1111-111111111111","username":"x","role":"{{role}}","createdAt":"2026-01-01T00:00:00Z","lastLoginAt":"2026-01-01T00:00:00Z"}"""));
        return new IdentityApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8090") });
    }

    [Fact]
    public void UserId_ParsesGuidFromHeader() {
        var currentUser = new CurrentUser(ClientReturningRole("viewer"));
        var ctx = ContextWithUserId("11111111-1111-1111-1111-111111111111");
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), currentUser.UserId(ctx));
    }

    [Fact]
    public void UserId_MissingHeader_ReturnsNull() {
        var currentUser = new CurrentUser(ClientReturningRole("viewer"));
        Assert.Null(currentUser.UserId(ContextWithUserId(null)));
    }

    [Fact]
    public async Task IsAtLeastAsync_AdminRole_SatisfiesAdminRequirement() {
        var currentUser = new CurrentUser(ClientReturningRole("admin"));
        var ctx = ContextWithUserId("11111111-1111-1111-1111-111111111111");
        Assert.True(await currentUser.IsAtLeastAsync(ctx, UserRole.Admin, CancellationToken.None));
    }

    [Fact]
    public async Task IsAtLeastAsync_ViewerRole_FailsAdminRequirement() {
        var currentUser = new CurrentUser(ClientReturningRole("viewer"));
        var ctx = ContextWithUserId("11111111-1111-1111-1111-111111111111");
        Assert.False(await currentUser.IsAtLeastAsync(ctx, UserRole.Admin, CancellationToken.None));
    }

    [Fact]
    public async Task IsAtLeastAsync_NoUserIdHeader_FailsEvenViewerRequirement() {
        var currentUser = new CurrentUser(ClientReturningRole("admin"));
        var ctx = ContextWithUserId(null);
        Assert.False(await currentUser.IsAtLeastAsync(ctx, UserRole.Viewer, CancellationToken.None));
    }

    [Fact]
    public async Task RoleAsync_CachesOnHttpContextItems_OneCallPerRequest() {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ => {
            calls++;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"userId":"11111111-1111-1111-1111-111111111111","username":"x","role":"admin","createdAt":"2026-01-01T00:00:00Z","lastLoginAt":"2026-01-01T00:00:00Z"}""");
        });
        var client = new IdentityApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8090") });
        var currentUser = new CurrentUser(client);
        var ctx = ContextWithUserId("11111111-1111-1111-1111-111111111111");

        await currentUser.RoleAsync(ctx, CancellationToken.None);
        await currentUser.RoleAsync(ctx, CancellationToken.None);

        Assert.Equal(1, calls);
    }
}
