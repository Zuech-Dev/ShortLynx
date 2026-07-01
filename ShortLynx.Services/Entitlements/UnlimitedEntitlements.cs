namespace ShortLynx.Services.Entitlements;

/// <summary>
/// The open-source default: grants everything, unlimited. This is what keeps a self-hosted install free
/// and complete at every tier. Stateless — safe to register as a singleton. A hosted deployment swaps in
/// a billing-backed <see cref="IEntitlements"/>; do not add enforcement here.
/// </summary>
public sealed class UnlimitedEntitlements : IEntitlements
{
    public Task<bool> CanCreateLinkAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default)
        => Task.FromResult(true);
}
