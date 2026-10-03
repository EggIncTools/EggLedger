using EggIdentity.Auth;
using EggLedger.Web.Server.Auth;
using EggLedger.Web.Server.Sync.Auth;

namespace EggLedger.Web.Server.Tests.Auth;

public sealed class AdminAccessTests {
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly AdminAccess Access = new();

    [Theory]
    [InlineData("admin", true)]
    [InlineData("viewer", false)]
    public void Ledger_cookie_principal_resolves_role_and_id(string role, bool admin) {
        var principal = AuthEndpoints.CookiePrincipal(UserId, "egg-user", role, "54321");

        Assert.Equal(admin, Access.IsAdmin(principal));
        Assert.Equal(UserId, Access.CurrentUserId(principal));
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("viewer", false)]
    public void EggIdentity_session_principal_resolves_role_and_id(string role, bool admin) {
        var options = new SessionCookieOptions { SigningSecret = "test-signing-secret-that-is-plenty-long-0123456789" };
        var token = SessionToken.Issue(options, new SessionUser(UserId.ToString(), "sid", role, "egg-user", null, "54321"), Now);
        var principal = SessionToken.Validate(options, token, Now);

        Assert.NotNull(principal);
        Assert.Equal(admin, Access.IsAdmin(principal));
        Assert.Equal(UserId, Access.CurrentUserId(principal));
    }

    [Fact]
    public void Anonymous_principal_is_not_admin() {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        Assert.False(Access.IsAdmin(principal));
        Assert.Null(Access.CurrentUserId(principal));
    }
}
