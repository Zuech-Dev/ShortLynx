namespace ShortLynx.Core.Models.Requests;

/// <summary>One recipient to mint a code for: a caller-generated UserId plus an optional human label.</summary>
public sealed record CodeRecipientRequest(Guid UserId, string? Recipient = null);

/// <summary>
/// Exactly one of <see cref="UserIds"/> (back-compat: bare ids, no labels, never one-time) or
/// <see cref="Recipients"/> (labels + <see cref="IsOneTimeUse"/>) must be supplied. Validated in the
/// controller rather than with attributes, since "exactly one of two optional fields" isn't expressible
/// with a single data-annotation.
/// </summary>
public sealed record CreateUserCodesRequest(
    Guid[]? UserIds = null,
    CodeRecipientRequest[]? Recipients = null,
    bool IsOneTimeUse = false);
