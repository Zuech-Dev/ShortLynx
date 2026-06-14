namespace ShortLynx.Services.Redirect;

public interface IRedirectService
{
    Task<RedirectCacheEntry?> LookupAsync(string code, CancellationToken ct = default);
}
