using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Links;

/// <summary>
/// Builds the public short URL for a link/code with the same precedence the redirect uses: a verified
/// pinned custom domain wins, otherwise the configured public base URL. Falls back to the bare code when
/// no base URL is configured (matches the dashboard's behavior).
/// </summary>
public static class ShortUrlBuilder
{
    public static async Task<string> BuildAsync(
        ShortLynxDbContext db, LinkEntity link, string code, string? publicBaseUrl, CancellationToken ct = default)
    {
        if (link.CustomDomainId is { } domainId)
        {
            var host = await db.CustomDomainEntities
                .Where(d => d.Id == domainId && d.VerificationStatus == DomainVerificationStatus.Verified)
                .Select(d => d.Domain)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(host))
                return $"https://{host}/{code}";
        }

        return string.IsNullOrWhiteSpace(publicBaseUrl) ? code : $"{publicBaseUrl!.TrimEnd('/')}/{code}";
    }
}
