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
    Guid? CampaignId = null);
