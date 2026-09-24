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
    ILogger<BrowserTimeZoneProvider> logger,
    IdentityApiClient? identity = null,
    SessionCookieOptions? sessionOptions = null) : IUserTimeZoneProvider {
    private const string ModulePath = "./_content/EggLedger.Web/js/timezone.js";
    private readonly IdentityApiClient? _identity = identity;
    private readonly ILogger<BrowserTimeZoneProvider> _logger = logger;
    private readonly bool _hadCookie = httpContextAccessor.HttpContext?.Request.Cookies.ContainsKey("tz") ?? true;
    private readonly string? _sessionToken = sessionOptions is { } eggIdentitySession
        ? httpContextAccessor.HttpContext?.Request.Cookies[eggIdentitySession.CookieName]
        : null;

    public TimeZoneInfo TimeZone { get; private set; } =
        Resolve(httpContextAccessor.HttpContext?.Request.Cookies["tz"], logger);

    public async Task EnsureUpToDateAsync() {
        if (await TryGetProfileTimeZoneAsync() is { } profileTz && !ReferenceEquals(profileTz, TimeZone)) {
            TimeZone = profileTz;
            try {
                var profileModule = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
                await profileModule.InvokeVoidAsync("setCookie", profileTz.Id);
                nav.NavigateTo(nav.Uri, forceLoad: true);
            } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
                _logger.LogDebug(ex, "timezone: failed to apply profile timezone cookie");
            }
            return;
        }

        if (_hadCookie) {
            return;
        }

        bool didSet;
        try {
            var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            didSet = await module.InvokeAsync<bool>("ensureCookie");
        } catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or TaskCanceledException) {
            _logger.LogDebug(ex, "timezone: failed to ensure browser timezone cookie");
            didSet = false;
        }

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
        } catch (HttpRequestException ex) {
            _logger.LogDebug(ex, "timezone: profile timezone fetch failed");
            return null;
        } catch (TaskCanceledException ex) {
            _logger.LogDebug(ex, "timezone: profile timezone fetch cancelled");
            return null;
        }
        return string.IsNullOrEmpty(profile?.Timezone) ? null : ResolveOrNull(profile.Timezone, _logger);
    }

    private static TimeZoneInfo? ResolveOrNull(string id, ILogger logger) {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        } catch (TimeZoneNotFoundException ex) {
            logger.LogDebug(ex, "timezone: unknown profile timezone {TimeZoneId}", id);
            return null;
        } catch (InvalidTimeZoneException ex) {
            logger.LogDebug(ex, "timezone: invalid profile timezone {TimeZoneId}", id);
            return null;
        }
    }

    private static TimeZoneInfo Resolve(string? id, ILogger logger) {
        if (string.IsNullOrEmpty(id)) {
            return TimeZoneInfo.Utc;
        }
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        } catch (TimeZoneNotFoundException ex) {
            logger.LogDebug(ex, "timezone: unknown cookie timezone {TimeZoneId}, using UTC", id);
            return TimeZoneInfo.Utc;
        } catch (InvalidTimeZoneException ex) {
            logger.LogDebug(ex, "timezone: invalid cookie timezone {TimeZoneId}, using UTC", id);
            return TimeZoneInfo.Utc;
        }
    }
}
