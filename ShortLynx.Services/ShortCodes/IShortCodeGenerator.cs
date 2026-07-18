namespace ShortLynx.Services.ShortCodes;

public interface IShortCodeGenerator
{
    /// <summary>
    /// Produces a candidate short code for a link. <paramref name="discriminator"/> is what makes
    /// multiple codes for the SAME link distinct — the recipient's user id for user-attributed codes,
    /// the social post's id for per-post publishing codes, or null for a link's single shared code.
    /// Generation is deterministic in (linkId, discriminator, attempt), so re-minting for the same pair
    /// is idempotent; callers increment <paramref name="attempt"/> on collision.
    /// </summary>
    string Generate(Guid linkId, Guid? discriminator = null, int attempt = 0);
}
