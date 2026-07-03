using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Social;

/// <summary>
/// Runs a connector action on behalf of a stored connection, handling access-token expiry in one place:
/// on <see cref="TokenExpiredException"/> it refreshes via the connector, persists the rotated pair
/// (encrypted), and retries the action exactly once. When the platform has no refresh path (or it also
/// fails), a <see cref="TokenExpiredException"/> with a "reconnect" message propagates to the caller.
/// Shared by publishing and metrics pulls so the subtle rotation logic exists once.
/// </summary>
internal static class ConnectorTokenGuard
{
    public static async Task<T> ExecuteAsync<T>(
        ShortLynxDbContext db,
        ITokenProtector protector,
        ISocialConnector connector,
        SocialConnectionEntity connection,
        Func<SocialConnectionContext, Task<T>> action,
        CancellationToken ct)
    {
        try
        {
            return await action(BuildContext(protector, connection));
        }
        catch (TokenExpiredException)
        {
            var rotated = await connector.RefreshAsync(BuildContext(protector, connection), ct);
            if (rotated is null)
                throw new TokenExpiredException("The connection's tokens have expired — reconnect the account.");

            connection.AccessTokenProtected = protector.Protect(rotated.AccessToken);
            connection.RefreshTokenProtected = rotated.RefreshToken is null ? null : protector.Protect(rotated.RefreshToken);
            await db.SaveChangesAsync(ct);

            return await action(BuildContext(protector, connection));
        }
    }

    public static SocialConnectionContext BuildContext(ITokenProtector protector, SocialConnectionEntity connection) => new(
        connection.ExternalAccountId,
        connection.Handle,
        connection.InstanceUrl,
        new SocialTokens(
            protector.Unprotect(connection.AccessTokenProtected),
            connection.RefreshTokenProtected is null ? null : protector.Unprotect(connection.RefreshTokenProtected)));
}
