namespace ShortLynx.Services.Analytics;

/// <summary>What GeoIP resolution is allowed to yield (MASTER_PLAN P1, amended by CITY_GEO_PLAN.md
/// for city, amended again for state/region as a generalization fallback — see CITY_GEO_PLAN.md's
/// latest revision): country (ISO-3166 alpha-2), IANA timezone, and — opt-in per account
/// (<c>AccountEntity.EnableCityAggregates</c>) — city and state/region (ISO-3166-2 subdivision
/// code). Coordinates and postal/zip code are never resolved anywhere in this codebase: each
/// amendment so far covers exactly the granularity it was reasoned through for; this is not a green
/// light for finer granularity later without the same process.
/// <see cref="City"/>/<see cref="State"/> never reach <c>VisitEntity</c> or <c>UserVisitEntity</c> —
/// see <c>CityClickDailyEntity</c> for where they're actually allowed to land.</summary>
public sealed record GeoLocation(string? Country = null, string? TimeZone = null, string? City = null, string? State = null)
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
    /// <param name="includeCity">Pass true only for an event belonging to an account with
    /// <c>EnableCityAggregates</c> on — the resolver has no account context of its own, so every
    /// caller must decide explicitly. Defaults false: the country+timezone-only call sites (the
    /// VisitEntity/UserVisitEntity write path) never need to change to keep today's behavior.
    /// <see cref="GeoLocation.State"/> is resolved whenever <see cref="GeoLocation.Country"/> is
    /// (regardless of this flag) so the city→state→country generalization cascade has state
    /// available even on the — common, not rare — response where city itself is empty.</param>
    GeoLocation Resolve(string rawIp, bool includeCity = false);
}

/// <summary>No-op default: no GeoIP database configured, so country/timezone/city are all left unset.</summary>
public sealed class NullGeoIpResolver : IGeoIpResolver
{
    public GeoLocation Resolve(string rawIp, bool includeCity = false) => GeoLocation.Empty;
}
