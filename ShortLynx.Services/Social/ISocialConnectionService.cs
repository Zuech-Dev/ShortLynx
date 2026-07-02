using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Account-scoped management of connected social accounts. Validates credentials through the platform's
/// <see cref="ISocialConnector"/>, encrypts tokens via <see cref="ITokenProtector"/> before they touch
/// the database, and enforces the plan seam (social publishing is an entitlement-gated feature).
/// </summary>
public interface ISocialConnectionService
{
    /// <summary>
    /// Connects (or re-connects) a social account. Upserts by (account, platform, external id), so
    /// reconnecting refreshes tokens/handle instead of duplicating. Throws <see cref="ArgumentException"/>
    /// for rejected credentials or an unsupported platform.
    /// </summary>
    Task<SocialConnectionEntity> ConnectAsync(
        Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
        SocialCredentials credentials, CancellationToken ct = default);

    Task<IReadOnlyList<SocialConnectionEntity>> ListAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Removes a connection (tokens are destroyed with the row). False if not the account's.</summary>
    Task<bool> DisconnectAsync(Guid connectionId, Guid accountId, CancellationToken ct = default);
}
