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
    // OAuth connectors register as concrete typed clients (a single IOAuthSocialConnector registration
    // can't hold two implementations); the endpoints resolve them per-platform out of the connector set.
    [InlineData(typeof(ShortLynx.Services.Social.ThreadsConnector))]
    [InlineData(typeof(ShortLynx.Services.Social.RedditConnector))]
    public void PageService_IsRegistered(Type serviceType)
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    [Fact]
    public void ConnectorSet_ResolvesOAuthConnector_PerPlatform()
    {
        using var scope = _factory.Services.CreateScope();
        var connectors = scope.ServiceProvider
            .GetRequiredService<IEnumerable<ShortLynx.Services.Social.ISocialConnector>>()
            .ToList();

        // The per-platform resolution the OAuth endpoints depend on — a regression here would surface
        // as the wrong platform's consent screen, not a DI error.
        Assert.Equal(ShortLynx.Data.Enums.SocialPlatform.Threads,
            ShortLynx.Services.Social.OAuthConnectorResolver.Require(connectors, ShortLynx.Data.Enums.SocialPlatform.Threads).Platform);
        Assert.Equal(ShortLynx.Data.Enums.SocialPlatform.Reddit,
            ShortLynx.Services.Social.OAuthConnectorResolver.Require(connectors, ShortLynx.Data.Enums.SocialPlatform.Reddit).Platform);
    }
}
