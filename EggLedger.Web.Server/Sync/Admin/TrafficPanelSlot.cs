using EggIdentity.Metrics;
using EggLedger.Web.Components.Admin;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Sync.Admin;

public sealed class TrafficPanelSlot : ITrafficPanelSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<TrafficPanel>(0);
        builder.AddAttribute(1, "RefreshIntervalSeconds", 5);
        builder.CloseComponent();
    };
}
