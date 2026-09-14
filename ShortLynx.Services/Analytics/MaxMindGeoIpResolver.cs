using MaxMind.GeoIP2;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// GeoLite2-City-backed resolver. Reads country + IANA timezone always, state/region whenever
/// country resolves, and city only when a caller asks for it (see <see cref="Resolve"/>) — never
/// coordinates or postal/zip code, which stay dropped here at the boundary regardless, per
/// MASTER_PLAN P1 (amended by CITY_GEO_PLAN.md for city, amended again for state as a
/// generalization fallback). Register as a singleton: <see cref="DatabaseReader"/> is thread-safe
/// and memory-maps the file. The database is a free download from MaxMind (account required); see
/// VisitSink:GeoIpDatabasePath.
/// </summary>
public sealed class MaxMindGeoIpResolver(string databasePath) : IGeoIpResolver, IDisposable
{
    private readonly DatabaseReader _reader = new(databasePath);

    public GeoLocation Resolve(string rawIp, bool includeCity = false)
    {
        // TryCity handles private ranges, malformed input, and addresses absent from the database.
        if (!_reader.TryCity(rawIp, out var city) || city is null)
            return GeoLocation.Empty;

        return new GeoLocation(
            Country: NullIfEmpty(city.Country.IsoCode),
            TimeZone: city.Location.TimeZone,
            City: includeCity ? NullIfEmpty(city.City.Name) : null,
            // MostSpecificSubdivision.IsoCode, not .Name: the package's own docs warn against using
            // subdivision names as a key/identity (not guaranteed unique/stable the way IsoCode is)
            // — the same reason Country already stores IsoCode, not Country.Name, above.
            State: NullIfEmpty(city.MostSpecificSubdivision.IsoCode));
    }

    // MaxMind returns an empty Subdivision/City/Country object (never null) when the database has no
    // answer, which surfaces as an empty string on .Name/.IsoCode, not null -- normalize both to the
    // same "absent" representation so every caller can rely on a plain null check.
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public void Dispose() => _reader.Dispose();
}
