using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Create a link in the current account. Mode is "Anonymous" (default) or "UserAttributed".
/// Optionally assign it to one of your campaigns at creation via CampaignId. CustomCode requests an
/// operator-chosen vanity code (Anonymous mode only; paid on the hosted service).</summary>
public sealed record CreateMyLinkRequest([Required] string Url, string? Mode = null, Guid? CampaignId = null, string? CustomCode = null);

/// <summary>Mint an API key for the current account.</summary>
public sealed record CreateMyApiKeyRequest([Required, MinLength(1)] string Name, string[] Scopes);

/// <summary>Invite a user to the current account at the given role (Owner/Admin/Member/Viewer).</summary>
public sealed record InviteMemberRequest([Required, EmailAddress] string Email, [Required] string Role);

/// <summary>Change an existing member's role in the current account.</summary>
public sealed record ChangeMemberRoleRequest([Required] string Role);

/// <summary>
/// Rename the current account and set the URLs the Mode 2 (user-attributed) disclosure interstitial
/// links to. PrivacyPolicyUrl/TermsOfServiceUrl are optional -- empty/whitespace clears the field
/// (interstitial falls back to ShortLynx's own default disclosure); a non-empty value must be a real
/// https:// URL, validated in AccountService rather than here so "clear the field" and "malformed URL"
/// aren't both rejected by a blanket [Url] attribute. ConfirmsDisclosure must be true whenever
/// PrivacyPolicyUrl is being set to a non-empty value -- same requirement Admin's own Settings page
/// has always enforced before this endpoint existed; setting a policy URL turns off the recipient-facing
/// disclosure interstitial, so it isn't accepted without an explicit acknowledgement of that.
/// </summary>
public sealed record UpdateAccountRequest(
    [Required, MinLength(1)] string Name, string? PrivacyPolicyUrl, string? TermsOfServiceUrl,
    bool ConfirmsDisclosure = false,
    // Full-replace like every other field here: a client must resend the account's current value on
    // every update, not just when changing it. Requires PrivacyPolicyUrl to be set (enforced in
    // AccountService) -- see CITY_GEO_PLAN.md §6.3.
    bool EnableCityAggregates = false);

/// <summary>Sets the current user's nav style preference ("Hamburger" or "HorizontalScroll").</summary>
public sealed record UpdatePreferencesRequest([Required] string NavStyle);
