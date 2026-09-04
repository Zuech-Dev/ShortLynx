using ShortLynx.Services.Analytics;

namespace ShortLynx.Core.Models.Responses;

public sealed record CodeClickStats(
    string Code,
    Guid? UserId,
    long ClickCount,
    // Null for an anonymous link's shared code (no recipient concept) and for codes minted via the
    // legacy bare-userIds request shape. Appended last for the same additive reason as
    // LinkResponse.CampaignId.
    string? Recipient = null);

public sealed record LinkAnalyticsResponse(
    Guid LinkId,
    string Url,
    string Mode,
    long TotalClicks,
    // Distinct hashed IPs. The IP hash rotates daily by design (privacy: limits cross-time linkage) at
    // a 5am Eastern boundary, so this is "distinct clickers per day, summed" — a returning visitor on a
    // later day counts again. It dedupes rapid repeat clicks (double-taps, prefetch) within the day, not
    // lifetime uniques.
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
    IReadOnlyList<HourlyClicks> HourlyDistribution,
    // Empty unless the account has EnableCityAggregates on (opt-in, off by default) — see
    // CityAggregator. k-anonymised on unique visitors, not raw clicks, at a lower threshold than every
    // other dimension here because it's the most re-identifying one.
    IReadOnlyList<CityCount> Cities);
