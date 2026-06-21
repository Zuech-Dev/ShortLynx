using DnsClient;

namespace ShortLynx.Services.Domains;

/// <summary>Production <see cref="IDnsResolver"/> backed by DnsClient.NET.</summary>
public sealed class DnsClientResolver : IDnsResolver
{
    private readonly ILookupClient _client = new LookupClient();

    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: ct);
            return result.Answers.TxtRecords()
                .SelectMany(r => r.Text)
                .ToList();
        }
        catch (DnsResponseException)
        {
            // NXDOMAIN / SERVFAIL / timeout — treat as "no records" so verification simply fails.
            return [];
        }
    }
}
