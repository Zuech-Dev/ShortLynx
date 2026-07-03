using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>
/// Connect a social account to the current account. Platform is "Bluesky" (or "Mastodon" once its
/// connector lands). For Bluesky: Identifier = handle/email, Secret = an app password (Settings →
/// App Passwords — never your main password); InstanceUrl only for a self-hosted PDS.
/// </summary>
public sealed record ConnectSocialRequest(
    [Required] string Platform,
    [Required] string Identifier,
    [Required] string Secret,
    string? InstanceUrl = null);

/// <summary>
/// Publish a link to one or more connected social accounts. The link's short URL is appended to Text
/// automatically unless Text already contains it; empty Text posts just the short URL.
/// </summary>
public sealed record PublishLinkRequest(
    [Required, MinLength(1)] Guid[] ConnectionIds,
    string? Text = null);
