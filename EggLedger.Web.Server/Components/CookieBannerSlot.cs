using EggIdentity.Auth;
using EggIdentity.Consent;
using EggLedger.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EggLedger.Web.Server.Components;

public sealed class CookieBannerSlot(IHttpContextAccessor accessor, SessionCookieOptions session) : ICookieBannerSlot {
    public RenderFragment Render() => builder => {
        var token = accessor.HttpContext?.Request.Cookies[session.CookieName];
        builder.OpenComponent<CookieBanner>(0);
        builder.AddComponentParameter(1, "SessionToken", string.IsNullOrEmpty(token) ? null : token);
        builder.AddComponentParameter(2, "PrivacyUrl", "/privacy");
        builder.CloseComponent();
    };
}
