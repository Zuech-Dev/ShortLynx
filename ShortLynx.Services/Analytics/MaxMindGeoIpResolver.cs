using MaxMind.GeoIP2;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// GeoLite2-City-backed resolver. Reads country + IANA timezone always, and city only when a caller
/// asks for it (see <see cref="Resolve"/>) — never region, coordinates, or postal/zip code, which stay
/// dropped here at the boundary regardless, per MASTER_PLAN P1 (amended by CITY_GEO_PLAN.md for city
/// specifically). Register as a singleton: <see cref="DatabaseReader"/> is thread-safe and memory-maps
/// the file. The database is a free download from MaxMind (account required); see
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
            Country: city.Country.IsoCode,
            TimeZone: city.Location.TimeZone,
            City: includeCity ? city.City.Name : null);
    }

    public void Dispose() => _reader.Dispose();
}
