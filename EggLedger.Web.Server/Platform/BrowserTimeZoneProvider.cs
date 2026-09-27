using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.UI;
using EggLedger.Web.Platform;

namespace EggLedger.Web.Server.Platform;

public sealed class BrowserTimeZoneProvider(
    BrowserTimeZone browser,
    IHttpContextAccessor httpContextAccessor,
    ILogger<BrowserTimeZoneProvider> logger,
    IdentityApiClient? identity = null,
    SessionCookieOptions? sessionOptions = null) : IUserTimeZoneProvider {
    private readonly string? _sessionToken = sessionOptions is { } eggIdentitySession
        ? httpContextAccessor.HttpContext?.Request.Cookies[eggIdentitySession.CookieName]
        : null;

    public TimeZoneInfo TimeZone => browser.Zone;

    public event Action? Changed {
        add => browser.Changed += value;
        remove => browser.Changed -= value;
    }

    public async Task EnsureUpToDateAsync() => await browser.SyncAsync(await TryGetProfileTimeZoneAsync());

    private async Task<string?> TryGetProfileTimeZoneAsync() {
        if (identity is null || string.IsNullOrEmpty(_sessionToken)) {
            return null;
        }

        try {
            var profile = await identity.GetProfileAsync(_sessionToken, CancellationToken.None);
            return string.IsNullOrEmpty(profile?.Timezone) ? null : profile.Timezone;
        } catch (HttpRequestException ex) {
            logger.LogDebug(ex, "timezone: profile timezone fetch failed");
            return null;
        } catch (TaskCanceledException ex) {
            logger.LogDebug(ex, "timezone: profile timezone fetch cancelled");
            return null;
        }
    }
}
