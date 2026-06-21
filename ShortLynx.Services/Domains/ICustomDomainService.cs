using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Domains;

public interface ICustomDomainService
{
    /// <summary>
    /// Registers a custom domain for a user in the Pending state with a fresh verification token.
    /// Throws <see cref="InvalidOperationException"/> if the domain is already registered.
    /// </summary>
    Task<CustomDomainEntity> AddAsync(string domain, Guid userAccountId, CancellationToken ct = default);

    /// <summary>Lists a user's domains, newest first.</summary>
    Task<IReadOnlyList<CustomDomainEntity>> ListAsync(Guid userAccountId, CancellationToken ct = default);

    /// <summary>
    /// Checks the DNS TXT record for the domain and flips it to Verified (active) on a match, or
    /// Failed otherwise. Returns the updated entity, or null if the domain isn't owned by the user.
    /// </summary>
    Task<CustomDomainEntity?> VerifyAsync(Guid domainId, Guid userAccountId, CancellationToken ct = default);

    /// <summary>Removes a user's domain. Returns false if it doesn't exist or isn't theirs.</summary>
    Task<bool> RemoveAsync(Guid domainId, Guid userAccountId, CancellationToken ct = default);
}
