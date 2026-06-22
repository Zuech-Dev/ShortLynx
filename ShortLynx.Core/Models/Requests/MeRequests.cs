using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Create a link in the current account. Mode is "Anonymous" (default) or "UserAttributed".</summary>
public sealed record CreateMyLinkRequest([Required] string Url, string? Mode = null);

/// <summary>Mint an API key for the current account.</summary>
public sealed record CreateMyApiKeyRequest([Required, MinLength(1)] string Name, string[] Scopes);
