namespace ShortLynx.Services.Analytics;

/// <summary>What GeoIP resolution is allowed to yield (MASTER_PLAN P1): country (ISO-3166 alpha-2)
/// and IANA timezone only. The GeoLite2-City database also resolves region, city, and coordinates —
/// those are deliberately discarded before anything touches the write path, because sub-country
/// location combined with device/browser/language approaches a fingerprint in low-traffic contexts.
/// Timezone alone still enables "what local hour do people click" analysis.</summary>
public sealed record GeoLocation(string? Country = null, string? TimeZone = null)
{
    public static readonly GeoLocation Empty = new();
}

/// <summary>
/// Resolves a raw IP at ingest. The default implementation (<see cref="NullGeoIpResolver"/>) returns
/// nothing, so the pipeline runs without a GeoIP database; <see cref="MaxMindGeoIpResolver"/> is
/// swapped in by DI when VisitSink:GeoIpDatabasePath points at a GeoLite2 database file.
/// </summary>
public interface IGeoIpResolver
{
    GeoLocation Resolve(string rawIp);
}

/// <summary>No-op default: no GeoIP database configured, so country and timezone are left unset.</summary>
public sealed class NullGeoIpResolver : IGeoIpResolver
{
    public GeoLocation Resolve(string rawIp) => GeoLocation.Empty;
}
