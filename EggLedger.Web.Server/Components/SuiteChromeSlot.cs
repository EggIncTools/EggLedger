using EggIdentity.UI.Suite;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Components;

public sealed class SuiteChromeSlot : ISuiteChromeSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<SuiteSwitcher>(0);
        builder.AddComponentParameter(1, nameof(SuiteSwitcher.Placement), SuitePlacement.Inline);
        builder.CloseComponent();
        builder.OpenComponent<SuiteAccount>(2);
        builder.AddComponentParameter(3, nameof(SuiteAccount.Placement), SuitePlacement.Inline);
        builder.CloseComponent();
    };
}
