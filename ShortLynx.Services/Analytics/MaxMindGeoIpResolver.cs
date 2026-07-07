using MaxMind.GeoIP2;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// GeoLite2-City-backed resolver. Reads country + IANA timezone and nothing else — the city, region,
/// and coordinate fields the database also returns are dropped here, at the boundary, per MASTER_PLAN
/// P1. Register as a singleton: <see cref="DatabaseReader"/> is thread-safe and memory-maps the file.
/// The database is a free download from MaxMind (account required); see VisitSink:GeoIpDatabasePath.
/// </summary>
public sealed class MaxMindGeoIpResolver(string databasePath) : IGeoIpResolver, IDisposable
{
    private readonly DatabaseReader _reader = new(databasePath);

    public GeoLocation Resolve(string rawIp)
    {
        // TryCity handles private ranges, malformed input, and addresses absent from the database.
        if (!_reader.TryCity(rawIp, out var city) || city is null)
            return GeoLocation.Empty;

        return new GeoLocation(
            Country: city.Country.IsoCode,
            TimeZone: city.Location.TimeZone);
    }

    public void Dispose() => _reader.Dispose();
}
