using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Domains;

public interface ICustomDomainService
{
    /// <summary>
    /// Registers a custom domain for an account in the Pending state with a fresh verification token.
    /// Throws <see cref="InvalidOperationException"/> if the domain is already registered.
    /// </summary>
    Task<CustomDomainEntity> AddAsync(string domain, Guid accountId, Guid? addedByUserAccountId = null, CancellationToken ct = default);

    /// <summary>Lists an account's domains, newest first.</summary>
    Task<IReadOnlyList<CustomDomainEntity>> ListAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Checks the DNS TXT record for the domain and flips it to Verified (active) on a match, or
    /// Failed otherwise. Returns the updated entity, or null if the domain isn't owned by the account.
    /// </summary>
    Task<CustomDomainEntity?> VerifyAsync(Guid domainId, Guid accountId, CancellationToken ct = default);

    /// <summary>Removes an account's domain. Returns false if it doesn't exist or isn't theirs.</summary>
    Task<bool> RemoveAsync(Guid domainId, Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Re-checks every currently-Verified domain's TXT record and demotes any that no longer match to
    /// Failed + inactive (so pinned links stop resolving). System-wide; returns the number demoted.
    /// </summary>
    Task<int> RecheckVerifiedAsync(CancellationToken ct = default);
}
