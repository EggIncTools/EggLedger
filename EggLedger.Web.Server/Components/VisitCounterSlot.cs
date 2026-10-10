using EggIdentity.Visits;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Components;

public sealed class VisitCounterSlot : IVisitCounterSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<VisitCounter>(0);
        builder.CloseComponent();
    };
}
