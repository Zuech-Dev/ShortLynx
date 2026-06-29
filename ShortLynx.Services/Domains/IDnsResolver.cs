namespace ShortLynx.Services.Domains;

/// <summary>Abstracts DNS TXT-record lookups so domain verification can be unit-tested.</summary>
public interface IDnsResolver
{
    /// <summary>Returns the TXT record strings published at <paramref name="name"/> (empty if none/NXDOMAIN).</summary>
    Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default);
}
