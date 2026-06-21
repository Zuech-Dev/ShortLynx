namespace ShortLynx.Core.Models.Responses;

/// <summary>
/// A custom domain. <see cref="VerificationHost"/> and <see cref="VerificationTxtValue"/> are the
/// DNS TXT record the caller must create before verifying.
/// </summary>
public sealed record DomainResponse(
    Guid Id,
    string Domain,
    string Status,
    string VerificationHost,
    string VerificationTxtValue,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset CreatedAt);
