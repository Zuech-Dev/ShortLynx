using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Links;

/// <summary>
/// Builds the public short URL for a link/code with the same precedence the redirect uses: a verified
/// pinned custom domain wins, otherwise the configured public base URL. Falls back to the bare path when
/// no base URL is configured (matches the dashboard's behavior).
/// </summary>
public static class ShortUrlBuilder
{
    /// <param name="isCustom">
    /// Whether <paramref name="code"/> is a custom (vanity) code. Custom codes resolve only under
    /// <paramref name="customRoutePrefix"/> (see <c>RedirectService.LookupAsync</c>, which explicitly
    /// excludes them from the root <c>/{code}</c> route) — building the bare-code URL for one produces a
    /// link that 404s. Required rather than defaulted so every call site has to consciously answer this,
    /// the same way a missing answer here previously went unnoticed at four call sites at once.
    /// </param>
    /// <param name="customRoutePrefix">
    /// The configured <see cref="ShortCodes.ShortCodeOptions.CustomRoutePrefix"/> (e.g. <c>"c"</c>).
    /// Ignored when <paramref name="isCustom"/> is false, so a caller that never mints custom codes
    /// (e.g. social post codes) can pass <see cref="string.Empty"/>.
    /// </param>
    public static async Task<string> BuildAsync(
        ShortLynxDbContext db, LinkEntity link, string code, bool isCustom, string customRoutePrefix,
        string? publicBaseUrl, CancellationToken ct = default)
    {
        var prefix = customRoutePrefix.Trim('/');
        var path = isCustom && prefix.Length > 0 ? $"{prefix}/{code}" : code;

        if (link.CustomDomainId is { } domainId)
        {
            var host = await db.CustomDomainEntities
                .Where(d => d.Id == domainId && d.VerificationStatus == DomainVerificationStatus.Verified)
                .Select(d => d.Domain)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(host))
                return $"https://{host}/{path}";
        }

        return string.IsNullOrWhiteSpace(publicBaseUrl) ? path : $"{publicBaseUrl!.TrimEnd('/')}/{path}";
    }
}
