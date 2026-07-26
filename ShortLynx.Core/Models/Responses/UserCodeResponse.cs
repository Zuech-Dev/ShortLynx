namespace ShortLynx.Core.Models.Responses;

public sealed record UserCodeResponse(
    Guid UserId,
    string Code,
    // Appended last so existing positional-construction call sites and clients that deserialise this
    // shape keep working — same reasoning as LinkResponse.CampaignId.
    string? Recipient = null,
    bool IsOneTimeUse = false);
