using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Components;

public interface ICookieBannerSlot {
    RenderFragment Render();
}
