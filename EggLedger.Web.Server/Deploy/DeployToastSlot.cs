using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Deploy;

public sealed class DeployToastSlot : IDeployToastSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<EggLedger.Web.Server.Components.DeployToastStarter>(0);
        builder.CloseComponent();
    };
}
