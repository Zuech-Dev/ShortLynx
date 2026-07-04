using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Campaigns;
using ShortLynx.Services.Domains;
using ShortLynx.Services.Links;

namespace ShortLynx.Tests.Admin;

// Guards the Admin app's DI container: the dashboard pages resolve these services at runtime via
// IServiceScopeFactory, and a missing registration only surfaces as a page-load error (which is exactly
// how the Campaigns page shipped broken — ICampaignService wasn't registered, but the bUnit tests
// registered it themselves and so couldn't see the gap). This resolves them from the real host.
public class AdminServiceResolutionTests : IClassFixture<AdminFactory>
{
    private readonly AdminFactory _factory;
    public AdminServiceResolutionTests(AdminFactory factory) => _factory = factory;

    [Theory]
    [InlineData(typeof(ICampaignService))]
    [InlineData(typeof(ILinkService))]
    [InlineData(typeof(ICustomDomainService))]
    [InlineData(typeof(IAccountService))]
    [InlineData(typeof(ShortLynx.Services.Social.ISocialConnectionService))]
    [InlineData(typeof(ShortLynx.Services.Social.ISocialPublishService))]
    [InlineData(typeof(ShortLynx.Services.Social.ISocialMetricsService))]
    [InlineData(typeof(ShortLynx.Services.Social.ITokenProtector))]
    [InlineData(typeof(ShortLynx.Services.Social.IOAuthSocialConnector))]
    public void PageService_IsRegistered(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }
}
