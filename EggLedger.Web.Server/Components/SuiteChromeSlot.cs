using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Components;

public sealed class SuiteChromeSlot : ISuiteChromeSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<SuiteChrome>(0);
        builder.CloseComponent();
    };

    public RenderFragment RenderAccount() => builder => {
        builder.OpenComponent<SuiteAccountChrome>(0);
        builder.CloseComponent();
    };
}
