using ShortLynx.Services.Entitlements;

namespace ShortLynx.Tests.Services.Entitlements;

public class UnlimitedEntitlementsTests
{
    private readonly UnlimitedEntitlements _sut = new();

    [Fact]
    public async Task CanCreateLink_IsAlwaysTrue()
        => Assert.True(await _sut.CanCreateLinkAsync(Guid.CreateVersion7()));

    [Fact]
    public async Task CanCreateCustomCode_IsAlwaysTrue()
        => Assert.True(await _sut.CanCreateCustomCodeAsync(Guid.CreateVersion7()));

    [Theory]
    [InlineData(PlanFeature.CustomDomains)]
    [InlineData(PlanFeature.UserAttributedLinks)]
    [InlineData(PlanFeature.Campaigns)]
    [InlineData(PlanFeature.SocialPublishing)]
    [InlineData(PlanFeature.Conversions)]
    [InlineData(PlanFeature.ApiAccess)]
    public async Task EveryFeature_IsEnabled(PlanFeature feature)
        => Assert.True(await _sut.IsFeatureEnabledAsync(Guid.CreateVersion7(), feature));
}
