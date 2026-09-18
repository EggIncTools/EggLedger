using System.Security.Claims;
using EggIdentity.Visits;
using EggLedger.Web.Components.Admin;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Sync.Admin;

public sealed class VisitsPanelSlot(IAdminAccess access) : IVisitsPanelSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<VisitsPanel>(0);
        builder.AddComponentParameter(1, "Authorize", (Func<ClaimsPrincipal, bool>)access.IsAdmin);
        builder.CloseComponent();
    };
}
