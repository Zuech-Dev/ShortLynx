using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Create a campaign in the current account. UTM fields are an optional default template.</summary>
public sealed record CreateCampaignRequest(
    [Required] string Name,
    string? Description = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null);

/// <summary>Update a campaign's fields. All fields are replaced (UTM fields cleared when omitted/null).</summary>
public sealed record UpdateCampaignRequest(
    [Required] string Name,
    string? Description = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null);

/// <summary>Assigns a link to a campaign, or unassigns it when CampaignId is null.</summary>
public sealed record SetLinkCampaignRequest(Guid? CampaignId);
