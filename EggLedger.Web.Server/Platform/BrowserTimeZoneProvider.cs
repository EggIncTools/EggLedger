using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggLedger.Web.Platform;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EggLedger.Web.Server.Platform;

public sealed class BrowserTimeZoneProvider(
    IHttpContextAccessor httpContextAccessor,
    IJSRuntime js,
    NavigationManager nav,
    IServiceProvider services) : IUserTimeZoneProvider {
    private const string ModulePath = "./_content/EggLedger.Web/js/timezone.js";
    private readonly IdentityApiClient? _identity = services.GetService<IdentityApiClient>();
    private readonly bool _hadCookie = httpContextAccessor.HttpContext?.Request.Cookies.ContainsKey("tz") ?? true;
    private readonly string? _sessionToken = services.GetService<SessionCookieOptions>() is { } eggIdentitySession
        ? httpContextAccessor.HttpContext?.Request.Cookies[eggIdentitySession.CookieName]
        : null;

    public TimeZoneInfo TimeZone { get; private set; } =
        Resolve(httpContextAccessor.HttpContext?.Request.Cookies["tz"]);

    public async Task EnsureUpToDateAsync() {
        if (await TryGetProfileTimeZoneAsync() is { } profileTz && !ReferenceEquals(profileTz, TimeZone)) {
            TimeZone = profileTz;
            var profileModule = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await profileModule.InvokeVoidAsync("setCookie", profileTz.Id);
            nav.NavigateTo(nav.Uri, forceLoad: true);
            return;
        }

        if (_hadCookie) {
            return;
        }

        var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        var didSet = await module.InvokeAsync<bool>("ensureCookie");
        if (didSet) {
            nav.NavigateTo(nav.Uri, forceLoad: true);
        }
    }

    private async Task<TimeZoneInfo?> TryGetProfileTimeZoneAsync() {
        if (_identity is null || string.IsNullOrEmpty(_sessionToken)) {
            return null;
        }

        ProfileResponse? profile;
        try {
            profile = await _identity.GetProfileAsync(_sessionToken, CancellationToken.None);
        } catch (HttpRequestException) {
            return null;
        } catch (TaskCanceledException) {
            return null;
        }
        return string.IsNullOrEmpty(profile?.Timezone) ? null : ResolveOrNull(profile.Timezone);
    }

    private static TimeZoneInfo? ResolveOrNull(string id) {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        } catch (TimeZoneNotFoundException) {
            return null;
        } catch (InvalidTimeZoneException) {
            return null;
        }
    }

    private static TimeZoneInfo Resolve(string? id) {
        if (string.IsNullOrEmpty(id)) {
            return TimeZoneInfo.Utc;
        }
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        } catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.Utc;
        } catch (InvalidTimeZoneException) {
            return TimeZoneInfo.Utc;
        }
    }
}
