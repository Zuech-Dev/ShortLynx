namespace ShortLynx.Core.Models.Responses;

public sealed record CodeClickStats(
    string Code,
    Guid? UserId,
    long ClickCount);

/// <summary>Clicks attributed to one platform (see <c>ClickSource</c>).</summary>
public sealed record SourceCount(string Source, long Count);

/// <summary>Clicks from one device class (see <c>DeviceType</c>).</summary>
public sealed record DeviceCount(string Device, long Count);

/// <summary>Clicks on a single UTC calendar day, for the click-over-time series.</summary>
public sealed record DailyClicks(DateOnly Date, long Count);

public sealed record LinkAnalyticsResponse(
    Guid LinkId,
    string Url,
    string Mode,
    long TotalClicks,
    // Distinct hashed IPs. The IP hash rotates hourly by design (privacy: limits cross-time linkage),
    // so this is "distinct clickers per hour, summed" — a returning visitor in a later hour counts
    // again. It dedupes rapid repeat clicks (double-taps, prefetch) within the hour, not lifetime uniques.
    long UniqueClicks,
    DateTimeOffset? FirstClickAt,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<CodeClickStats> Codes,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<DeviceCount> Devices,
    IReadOnlyList<DailyClicks> Timeline);
