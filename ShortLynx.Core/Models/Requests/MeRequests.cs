using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Create a link in the current account. Mode is "Anonymous" (default) or "UserAttributed".
/// Optionally assign it to one of your campaigns at creation via CampaignId.</summary>
public sealed record CreateMyLinkRequest([Required] string Url, string? Mode = null, Guid? CampaignId = null);

/// <summary>Mint an API key for the current account.</summary>
public sealed record CreateMyApiKeyRequest([Required, MinLength(1)] string Name, string[] Scopes);

/// <summary>Invite a user to the current account at the given role (Owner/Admin/Member/Viewer).</summary>
public sealed record InviteMemberRequest([Required, EmailAddress] string Email, [Required] string Role);

/// <summary>Change an existing member's role in the current account.</summary>
public sealed record ChangeMemberRoleRequest([Required] string Role);
