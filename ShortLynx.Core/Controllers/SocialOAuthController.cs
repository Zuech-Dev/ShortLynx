using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Social;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// Browser-redirect OAuth for platforms that connect via a consent screen rather than user-supplied
/// credentials (Threads, Reddit; Bluesky/Mastodon go through <see cref="MeSocialController"/> instead).
/// Moved here from ShortLynx.Admin so the flow no longer depends on Admin's cookie session — Meta and
/// Reddit register a fixed redirect URI per app, so wherever this controller is reachable is now that
/// URI. One shared flow per platform: mint anti-CSRF state in a short-lived DataProtection-wrapped
/// cookie, bounce to the platform's consent screen, verify state on return, exchange the code, and
/// upsert the connection.
/// </summary>
[Route("social")]
public sealed class SocialOAuthController(
    IEnumerable<ISocialConnector> connectors,
    IDataProtectionProvider dataProtection,
    ISocialConnectionService socialConnections,
    IAccountService accounts,
    IOptions<ThreadsOptions> threadsOptions,
    IOptions<RedditOptions> redditOptions,
    IOptions<SocialOAuthOptions> oauthOptions) : SessionControllerBase
{
    // GET social/{slug}/authorize — sends the browser to the platform's consent screen. Unconfigured
    // deployments (no platform app yet — most self-hosters) must fail here with a clear redirect, not
    // send the browser onward with an empty client_id, which the platform answers with a generic error.
    [HttpGet("{slug}/authorize")]
    public IActionResult Authorize(string slug)
    {
        if (!TryResolvePlatform(slug, out var platform)) return NotFound();

        var (appId, appSecret, redirectUri) = CredentialsFor(platform);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            return ReturnError(platform, "not_configured");

        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var protector = dataProtection.CreateProtector(CookiePurpose(platform));
        Response.Cookies.Append(CookieName(platform), protector.Protect(state), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
        });

        return Redirect(OAuthConnectorResolver.Require(connectors, platform).BuildAuthorizeUrl(redirectUri, state));
    }

    // GET social/{slug}/callback — where the platform sends the browser back. Must exactly match the
    // redirect URI registered in the platform's app settings.
    [HttpGet("{slug}/callback")]
    public async Task<IActionResult> Callback(string slug, string? code, string? state, string? error, CancellationToken ct)
    {
        if (!TryResolvePlatform(slug, out var platform)) return NotFound();
        if (!string.IsNullOrEmpty(error)) return ReturnError(platform, error);

        var cookieName = CookieName(platform);
        var cookieValue = Request.Cookies[cookieName];
        Response.Cookies.Delete(cookieName); // single use either way

        if (string.IsNullOrEmpty(cookieValue) || string.IsNullOrEmpty(state))
            return ReturnError(platform, "missing_state");

        string expectedState;
        try
        {
            expectedState = dataProtection.CreateProtector(CookiePurpose(platform)).Unprotect(cookieValue);
        }
        catch (CryptographicException)
        {
            return ReturnError(platform, "invalid_state");
        }

        // Anti-CSRF: the value returned by the platform must match the one this same browser was handed
        // at /authorize — otherwise this could be a crafted callback URL in a victim's browser.
        if (!string.Equals(expectedState, state, StringComparison.Ordinal))
            return ReturnError(platform, "state_mismatch");
        if (string.IsNullOrEmpty(code))
            return ReturnError(platform, "missing_code");

        // This endpoint *creates* a connection — same ManageResources gate MeSocialController.Connect
        // uses for the credential-based platforms, or a Viewer could complete the OAuth flow directly
        // and bypass the UI gate. Checked manually (not via [RequireAccountAction]) so a denial can
        // redirect the browser back to the dashboard with a readable error instead of a bare 403 JSON
        // body — this endpoint is a full-page navigation target, not an XHR call.
        var role = await accounts.GetRoleAsync(AccountId, CurrentUserId, ct);
        if (role is not { } r || !AccountPermissions.Can(r, AccountAction.ManageResources))
            return ReturnError(platform, "forbidden");

        try
        {
            var connector = OAuthConnectorResolver.Require(connectors, platform);
            var (_, _, redirectUri) = CredentialsFor(platform);
            var identity = await connector.ExchangeAuthorizationCodeAsync(code, redirectUri, ct);
            await socialConnections.ConnectFromIdentityAsync(AccountId, CurrentUserId, platform, identity, instanceUrl: null, ct);
        }
        catch (ArgumentException ex)
        {
            return ReturnError(platform, ex.Message);
        }
        catch (EntitlementException)
        {
            return ReturnError(platform, "plan");
        }

        return Redirect($"{oauthOptions.Value.ReturnUrlBase}?connected={slug}");
    }

    private (string AppId, string AppSecret, string RedirectUri) CredentialsFor(SocialPlatform platform) => platform switch
    {
        SocialPlatform.Threads => (threadsOptions.Value.AppId, threadsOptions.Value.AppSecret, threadsOptions.Value.RedirectUri),
        SocialPlatform.Reddit => (redditOptions.Value.AppId, redditOptions.Value.AppSecret, redditOptions.Value.RedirectUri),
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    private IActionResult ReturnError(SocialPlatform platform, string error)
        => Redirect($"{oauthOptions.Value.ReturnUrlBase}?{Slug(platform)}Error={Uri.EscapeDataString(error)}");

    private static bool TryResolvePlatform(string slug, out SocialPlatform platform)
        => Enum.TryParse(slug, ignoreCase: true, out platform) && platform is SocialPlatform.Threads or SocialPlatform.Reddit;

    private static string Slug(SocialPlatform platform) => platform.ToString().ToLowerInvariant();
    private static string CookieName(SocialPlatform platform) => $"sl_{Slug(platform)}_oauth_state";
    private static string CookiePurpose(SocialPlatform platform) => $"ShortLynx.{platform}OAuthState";
}
