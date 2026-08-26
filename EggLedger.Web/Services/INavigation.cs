using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Services;

public interface INavigation {
    void NavigateTo(string url);
}

public sealed class BlazorNavigation(NavigationManager nav) : INavigation {
    public void NavigateTo(string url) => nav.NavigateTo(url, forceLoad: true);
}
