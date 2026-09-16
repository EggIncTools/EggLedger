using EggLedger.Web.Components.Admin;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Deploy;

public sealed class DeployPanelSlot : IDeployPanelSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<EggLedger.Web.Server.Components.Admin.DeployPanelHost>(0);
        builder.CloseComponent();
    };
}
