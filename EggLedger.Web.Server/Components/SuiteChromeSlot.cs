using EggIdentity.UI.Suite;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Components;

public sealed class SuiteChromeSlot : ISuiteChromeSlot {
    public RenderFragment Render() => builder => {
        builder.OpenComponent<SuiteSwitcher>(0);
        builder.AddComponentParameter(1, nameof(SuiteSwitcher.Placement), SuitePlacement.Bar);
        builder.AddComponentParameter(2, nameof(SuiteSwitcher.ChildContent), (RenderFragment)(account => {
            account.OpenComponent<SuiteAccount>(0);
            account.AddComponentParameter(1, nameof(SuiteAccount.Placement), SuitePlacement.Bar);
            account.CloseComponent();
        }));
        builder.CloseComponent();
    };
}
