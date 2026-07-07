using ShortLynx.Services.Analytics;

namespace ShortLynx.Core.Models.Responses;

public sealed record CodeClickStats(
    string Code,
    Guid? UserId,
    long ClickCount);

public sealed record LinkAnalyticsResponse(
    Guid LinkId,
    string Url,
    string Mode,
    long TotalClicks,
    // Distinct hashed IPs. The IP hash rotates hourly by design (privacy: limits cross-time linkage),
    // so this is "distinct clickers per hour, summed" — a returning visitor in a later hour counts
    // again. It dedupes rapid repeat clicks (double-taps, prefetch) within the hour, not lifetime uniques.
    long UniqueClicks,
    // Bot traffic (crawlers, email-client link checkers) is detected at ingest; these split it out so
    // clients can report engagement from people rather than robots. HumanClicks + BotClicks = TotalClicks.
    long HumanClicks,
    long HumanUniqueClicks,
    long BotClicks,
    DateTimeOffset? FirstClickAt,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<CodeClickStats> Codes,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<DeviceCount> Devices,
    IReadOnlyList<DailyClicks> Timeline,
    // All 24 UTC hour-of-day buckets (zero-filled), for send-window analysis.
    IReadOnlyList<HourlyClicks> HourlyDistribution);
