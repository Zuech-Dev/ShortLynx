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
    double? P90TimeToFirstClickMinutes);

/// <summary>
/// Pure reduction from (code minted at, first click at) pairs — callers join UserLinkCodes to their
/// earliest UserVisit and pass the result, keeping this testable and provider-agnostic like
/// <see cref="ClickAggregator"/>.
/// </summary>
public static class RecipientEngagement
{
    public static RecipientEngagementStats Compute(
        IReadOnlyCollection<(DateTimeOffset CreatedAt, DateTimeOffset? FirstClickAt)> codes)
    {
        // Clock skew or backdated seeds can make FirstClickAt precede CreatedAt; clamp to zero
        // rather than reporting a negative reaction time.
        var minutes = codes
            .Where(c => c.FirstClickAt is not null)
            .Select(c => Math.Max(0, (c.FirstClickAt!.Value - c.CreatedAt).TotalMinutes))
            .OrderBy(m => m)
            .ToList();

        return new RecipientEngagementStats(
            RecipientsTotal: codes.Count,
            RecipientsClicked: minutes.Count,
            MedianTimeToFirstClickMinutes: Percentile(minutes, 0.5),
            P90TimeToFirstClickMinutes: Percentile(minutes, 0.9));
    }

    // Nearest-rank percentile on a pre-sorted list; null when there are no samples.
    private static double? Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return null;
        var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
