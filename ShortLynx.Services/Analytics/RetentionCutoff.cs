using ShortLynx.Services.Entitlements;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// The earliest click an account's plan lets it see. Retention is enforced by HIDING older clicks from
/// analytics, not deleting them: a downgrade narrows the window, an upgrade restores the history.
/// Null means unlimited — always the case on self-host (<see cref="UnlimitedEntitlements"/>).
/// </summary>
public static class RetentionCutoff
{
    public static async Task<DateTimeOffset?> ForAccountAsync(
        IEntitlements entitlements, Guid accountId, CancellationToken ct = default)
        => await entitlements.GetRetentionDaysAsync(accountId, ct) is { } days
            ? DateTimeOffset.UtcNow.AddDays(-days)
            : null;

    /// <summary>The later of a caller's requested start and the plan cutoff.</summary>
    public static DateTimeOffset Clamp(DateTimeOffset requested, DateTimeOffset? cutoff)
        => cutoff is { } c && c > requested ? c : requested;
}
