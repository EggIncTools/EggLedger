using EggIdentity.Settings.AdminUi;
using EggLedger.Web.Components.Admin;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Settings;

public sealed class SettingsPanelSlot : ISettingsPanelSlot {
    public RenderFragment Render(string? updatedBy) => builder => {
        builder.OpenComponent<SettingsPanel>(0);
        builder.AddComponentParameter(1, nameof(SettingsPanel.UpdatedBy), updatedBy);
        builder.CloseComponent();
    };
}
