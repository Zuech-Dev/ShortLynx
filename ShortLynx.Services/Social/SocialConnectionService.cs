using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Entitlements;

namespace ShortLynx.Services.Social;

public sealed class SocialConnectionService(
    ShortLynxDbContext db,
    IEnumerable<ISocialConnector> connectors,
    ITokenProtector protector,
    IEntitlements entitlements) : ISocialConnectionService
{
    public async Task<SocialConnectionEntity> ConnectAsync(
        Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
        SocialCredentials credentials, CancellationToken ct = default)
    {
        // Plan seam checked before ever talking to the platform: never denied under the OSS
        // UnlimitedEntitlements default; a hosted policy may gate it.
        await EnsureEntitledAsync(accountId, ct);

        var connector = connectors.FirstOrDefault(c => c.Platform == platform)
            ?? throw new ArgumentException($"No connector is available for '{platform}'.", nameof(platform));

        var identity = await connector.ConnectAsync(credentials, ct);
        return await UpsertAsync(accountId, connectedByUserAccountId, platform, identity, credentials.InstanceUrl, ct);
    }

    public async Task<SocialConnectionEntity> ConnectFromIdentityAsync(
        Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
        SocialIdentity identity, string? instanceUrl = null, CancellationToken ct = default)
    {
        // The OAuth callback has already talked to the platform (code exchanged for tokens) by the time
        // this runs, so the gate here just decides whether we persist the connection, not whether we
        // reach out — there's no "before contacting the platform" step to protect on this path.
        await EnsureEntitledAsync(accountId, ct);
        return await UpsertAsync(accountId, connectedByUserAccountId, platform, identity, instanceUrl, ct);
    }

    private async Task EnsureEntitledAsync(Guid accountId, CancellationToken ct)
    {
        if (!await entitlements.IsFeatureEnabledAsync(accountId, PlanFeature.SocialPublishing, ct))
            throw new EntitlementException("Social publishing is not available on your plan.");
    }

    private async Task<SocialConnectionEntity> UpsertAsync(
        Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
        SocialIdentity identity, string? instanceUrl, CancellationToken ct)
    {
        // Upsert by (account, platform, external id): reconnecting rotates tokens and refreshes the
        // handle rather than creating a duplicate row.
        var existing = await db.SocialConnectionEntities.FirstOrDefaultAsync(
            c => c.AccountId == accountId
              && c.Platform == platform
              && c.ExternalAccountId == identity.ExternalAccountId, ct);

        if (existing is not null)
        {
            existing.Handle = identity.Handle;
            existing.InstanceUrl = instanceUrl;
            existing.AccessTokenProtected = protector.Protect(identity.AccessToken);
            existing.RefreshTokenProtected = identity.RefreshToken is null ? null : protector.Protect(identity.RefreshToken);
            existing.ExpiresAt = identity.ExpiresAt;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var entity = new SocialConnectionEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            UserAccountId = connectedByUserAccountId,
            Platform = platform,
            ExternalAccountId = identity.ExternalAccountId,
            Handle = identity.Handle,
            InstanceUrl = instanceUrl,
            AccessTokenProtected = protector.Protect(identity.AccessToken),
            RefreshTokenProtected = identity.RefreshToken is null ? null : protector.Protect(identity.RefreshToken),
            ExpiresAt = identity.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.SocialConnectionEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<SocialConnectionEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
        => await db.SocialConnectionEntities
            .Where(c => c.AccountId == accountId)
            .OrderByDescending(c => c.Id)
            .ToListAsync(ct);

    public async Task<bool> DisconnectAsync(Guid connectionId, Guid accountId, CancellationToken ct = default)
    {
        var affected = await db.SocialConnectionEntities
            .Where(c => c.Id == connectionId && c.AccountId == accountId)
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }
}
