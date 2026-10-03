using System.Security.Claims;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggLedger.Web.Components.Admin;

namespace EggLedger.Web.Server.Auth;

public sealed class AdminAccess : IAdminAccess {
    public bool IsAdmin(ClaimsPrincipal user) => user.ToCurrentUser().IsAtLeast(UserRole.Admin);

    public Guid? CurrentUserId(ClaimsPrincipal user) => user.ToCurrentUser().Id;
}
