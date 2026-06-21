using ShortLynx.Services.Domains;

namespace ShortLynx.Tests.Stubs;

/// <summary>Test <see cref="IDnsResolver"/> whose TXT records are set explicitly per host.</summary>
public sealed class InMemoryDnsResolver : IDnsResolver
{
    public readonly Dictionary<string, List<string>> Records = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(string host, params string[] txtValues) => Records[host] = [.. txtValues];

    public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Records.TryGetValue(name, out var r) ? r : []);
}
