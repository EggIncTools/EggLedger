using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.Contract;
using Microsoft.AspNetCore.Http;

namespace EggLedger.Web.Server.Sync.Auth;







public interface ICurrentUser {
    Guid? UserId(HttpContext ctx);
    Task<string?> RoleAsync(HttpContext ctx, CancellationToken ct);
    Task<bool> IsAtLeastAsync(HttpContext ctx, UserRole role, CancellationToken ct);
}

public sealed class CurrentUser(IdentityApiClient identity) : ICurrentUser {
    private const string RoleItemsKey = "EggIdentity.Role";

    public Guid? UserId(HttpContext ctx) =>
        Guid.TryParse(ctx.Request.Headers[RequireAuth.UserIdHeader].ToString(), out var id) ? id : null;

    public async Task<string?> RoleAsync(HttpContext ctx, CancellationToken ct) {
        if (ctx.Items.TryGetValue(RoleItemsKey, out var cached)) return (string?)cached;
        var userId = UserId(ctx);
        if (userId is null) { ctx.Items[RoleItemsKey] = null; return null; }
        var user = await identity.GetAsync(userId.Value, ct);
        var role = user?.Role;
        ctx.Items[RoleItemsKey] = role;
        return role;
    }

    public async Task<bool> IsAtLeastAsync(HttpContext ctx, UserRole role, CancellationToken ct) {
        var rawRole = await RoleAsync(ctx, ct);
        if (rawRole is null) return false;
        return UserRoles.IsAtLeast(UserRoles.Parse(rawRole), role);
    }
}
