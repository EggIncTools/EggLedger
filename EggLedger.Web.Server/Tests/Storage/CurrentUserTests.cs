using System.Security.Claims;
using EggLedger.Web.Server.Auth;
using EggLedger.Web.Server.Storage;
using Microsoft.AspNetCore.Components.Authorization;

namespace EggLedger.Web.Server.Tests.Storage;

public class CurrentUserTests {
    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }

    [Fact]
    public async Task GetUserIdAsync_returns_null_when_unauthenticated() {
        var provider = new FakeAuthStateProvider(new ClaimsPrincipal(new ClaimsIdentity()));
        var sut = new CurrentUser(provider);
        Assert.Null(await sut.GetUserIdAsync());
    }

    [Fact]
    public async Task GetUserIdAsync_parses_claim_as_guid() {
        var id = Guid.NewGuid();
        var identity = new ClaimsIdentity([new Claim(AuthScheme.UserIdClaim, id.ToString())], "test");
        var provider = new FakeAuthStateProvider(new ClaimsPrincipal(identity));
        var sut = new CurrentUser(provider);
        Assert.Equal(id, await sut.GetUserIdAsync());
    }

    [Fact]
    public async Task RequireUserIdAsync_throws_when_unauthenticated() {
        var provider = new FakeAuthStateProvider(new ClaimsPrincipal(new ClaimsIdentity()));
        var sut = new CurrentUser(provider);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RequireUserIdAsync());
    }
}
