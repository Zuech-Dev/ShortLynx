namespace ShortLynx.Services.Redirect;

public interface IRedirectService
{
    /// <summary>
    /// Resolves a short code. <paramref name="host"/> is the request Host: a link pinned to a custom
    /// domain only resolves when the host matches; unpinned links resolve on any host.
    /// </summary>
    Task<RedirectCacheEntry?> LookupAsync(string code, string? host = null, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims a one-time code (flips IsUsed exactly once). Lookup never claims — the
    /// redirect handler calls this immediately before redirecting, after any disclosure choice,
    /// so showing the interstitial can't burn the code. False when already used (raced or replayed).
    /// </summary>
    Task<bool> TryClaimOneTimeAsync(Guid userLinkCodeId, CancellationToken ct = default);
}
