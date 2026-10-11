using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Components;

public sealed class LoginActionsSlot : ILoginActionsSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<LoginActions>(0);
        builder.CloseComponent();
    };
}
