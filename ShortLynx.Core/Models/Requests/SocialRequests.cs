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
