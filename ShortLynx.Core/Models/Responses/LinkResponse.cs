namespace ShortLynx.Core.Models.Responses;

public sealed record LinkResponse(
    Guid Id,
    string Url,
    string Mode,
    string ShortCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    // The campaign this link is grouped under, or null when ungrouped. Appended last so the change is
    // additive: existing clients deserialising this shape keep working, and positional construction
    // sites elsewhere don't shift.
    //
    // Present because PUT /me/links/{id}/campaign can set the assignment but nothing could read it
    // back — a client offering a campaign picker had no way to show which option was already chosen,
    // and defaulting the control to "none" silently misreports a grouped link as ungrouped.
    Guid? CampaignId = null,
    // Whether ShortCode is a custom (vanity) code, which resolves only under the account-configured
    // custom route prefix (ShortCodeOptions.CustomRoutePrefix, default "c") — never at the root
    // /{code} — see RedirectService.LookupAsync, which explicitly excludes custom codes from that
    // route. A client building a copyable/QR-encoded short URL from ShortCode needs this to route it
    // correctly; without it, a custom-coded link's displayed URL 404s. Always false for
    // user-attributed links, which have no shared ShortCode to begin with.
    bool IsCustom = false);
