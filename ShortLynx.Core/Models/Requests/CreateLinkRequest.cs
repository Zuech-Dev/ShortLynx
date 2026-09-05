using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>
/// <c>CustomCode</c> requests an operator-chosen vanity code (paid on the hosted service).
/// <c>TagIds</c> requires the <c>tags:write</c> scope in addition to <c>links:write</c> -- a key
/// without it gets 403, not a silently-ignored TagIds (a caller who thinks tagging happened when it
/// didn't is worse than an explicit rejection).
/// </summary>
public sealed record CreateLinkRequest(
    [Required, Url] string Url,
    string? CustomCode = null,
    Guid[]? TagIds = null);
