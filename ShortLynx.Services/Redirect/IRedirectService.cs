namespace ShortLynx.Services.Redirect;

public interface IRedirectService
{
    /// <summary>
    /// Resolves a short code. <paramref name="host"/> is the request Host: a link pinned to a custom
    /// domain only resolves when the host matches; unpinned links resolve on any host.
    /// </summary>
    Task<RedirectCacheEntry?> LookupAsync(string code, string? host = null, CancellationToken ct = default);
}
