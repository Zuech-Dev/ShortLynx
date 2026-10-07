using ShortLynx.Services.Entitlements;

namespace ShortLynx.Tests.Infrastructure;

/// <summary>
/// Configurable <see cref="IEntitlements"/> for gate tests: everything allowed by default (matching
/// self-host's <see cref="UnlimitedEntitlements"/>), with each check individually switchable to a denial.
/// </summary>
public sealed class FakeEntitlements : IEntitlements
{
    public bool AllowLinks { get; set; } = true;
    public bool AllowCustomCodes { get; set; } = true;
    public bool AllowCustomDomainSlot { get; set; } = true;
    public bool AllowMembers { get; set; } = true;
    public HashSet<PlanFeature> DisabledFeatures { get; } = [];
    public int? RetentionDays { get; set; }

    public static FakeEntitlements Without(params PlanFeature[] features)
    {
        var f = new FakeEntitlements();
        foreach (var feature in features) f.DisabledFeatures.Add(feature);
        return f;
    }

    public Task<bool> CanCreateLinkAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(AllowLinks);
    public Task<bool> CanCreateCustomCodeAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(AllowCustomCodes);
    public Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default)
        => Task.FromResult(!DisabledFeatures.Contains(feature));
    public Task<bool> CanAddCustomDomainAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(AllowCustomDomainSlot);
    public Task<int?> GetRetentionDaysAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(RetentionDays);
    public Task<bool> CanAddMemberAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(AllowMembers);
}
