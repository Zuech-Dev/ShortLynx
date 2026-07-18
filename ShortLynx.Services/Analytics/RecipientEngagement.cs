namespace ShortLynx.Services.Analytics;

/// <summary>
/// Mode 2 (user-attributed) engagement roll-up: how many provisioned recipient codes exist, how many
/// have been clicked at least once, and how quickly recipients click after their code is minted.
/// Times are minutes; the percentiles are null until at least one code has a click. This is operator-
/// facing attribution over codes the operator provisioned — no k-anonymity fold applies, because the
/// operator already holds the code→recipient mapping (see MASTER_PLAN P3).
/// </summary>
public sealed record RecipientEngagementStats(
    int RecipientsTotal,
    int RecipientsClicked,
    double? MedianTimeToFirstClickMinutes,
    double? P90TimeToFirstClickMinutes,
    // Repeat engagement. Unlike the anonymous-link repeat metrics (which are bounded to one hour by the
    // rotating IP hash), these are EXACT and unbounded in time: a recipient code identifies the person
    // the operator provisioned it for, so repeat clicks are just rows on that code — no hashing, no
    // dedup window. A recipient returning to a link over days is a real engagement signal for outreach.
    int RecipientsClickedMoreThanOnce = 0,
    double AverageClicksPerEngagedRecipient = 0,
    int MaxClicksBySingleRecipient = 0);

/// <summary>
/// Pure reduction from (code minted at, first click at, click count) tuples — callers join
/// UserLinkCodes to their earliest UserVisit and click count and pass the result, keeping this testable
/// and provider-agnostic like <see cref="ClickAggregator"/>.
/// </summary>
public static class RecipientEngagement
{
    public static RecipientEngagementStats Compute(
        IReadOnlyCollection<(DateTimeOffset CreatedAt, DateTimeOffset? FirstClickAt, long Clicks)> codes)
    {
        // Clock skew or backdated seeds can make FirstClickAt precede CreatedAt; clamp to zero
        // rather than reporting a negative reaction time.
        var minutes = codes
            .Where(c => c.FirstClickAt is not null)
            .Select(c => Math.Max(0, (c.FirstClickAt!.Value - c.CreatedAt).TotalMinutes))
            .OrderBy(m => m)
            .ToList();

        var engaged = codes.Where(c => c.Clicks > 0).Select(c => c.Clicks).ToList();

        return new RecipientEngagementStats(
            RecipientsTotal: codes.Count,
            RecipientsClicked: minutes.Count,
            MedianTimeToFirstClickMinutes: Percentile(minutes, 0.5),
            P90TimeToFirstClickMinutes: Percentile(minutes, 0.9),
            RecipientsClickedMoreThanOnce: engaged.Count(c => c > 1),
            AverageClicksPerEngagedRecipient: engaged.Count == 0
                ? 0
                : Math.Round(engaged.Sum() / (double)engaged.Count, 2),
            MaxClicksBySingleRecipient: engaged.Count == 0 ? 0 : (int)engaged.Max());
    }

    // Nearest-rank percentile on a pre-sorted list; null when there are no samples.
    private static double? Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return null;
        var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
