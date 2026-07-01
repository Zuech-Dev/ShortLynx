namespace ShortLynx.Services.Analytics;

/// <summary>
/// Resolves a raw IP to an ISO-3166 alpha-2 country code at ingest — country is low-entropy and useful,
/// while city + ISP/ASN would be re-identifying and are deliberately never retained. The default
/// implementation (<see cref="NullGeoIpResolver"/>) returns null, so the pipeline runs without a GeoIP
/// database; a local MaxMind GeoLite2 resolver can be swapped in without touching the write path.
/// </summary>
public interface IGeoIpResolver
{
    string? ResolveCountry(string rawIp);
}

/// <summary>No-op default: no GeoIP database configured, so country is left unset.</summary>
public sealed class NullGeoIpResolver : IGeoIpResolver
{
    public string? ResolveCountry(string rawIp) => null;
}
