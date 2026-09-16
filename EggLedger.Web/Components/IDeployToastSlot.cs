using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Components;

public interface IDeployToastSlot {
    RenderFragment Render();
}
