using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Components.Admin;

public interface ISettingsPanelSlot {
    RenderFragment Render(string? updatedBy);
}
