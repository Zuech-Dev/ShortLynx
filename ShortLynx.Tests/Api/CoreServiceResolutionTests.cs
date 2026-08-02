using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Api;

// Guards Core's DI container the same way AdminServiceResolutionTests guards Admin's: SocialOAuthController
// resolves ISocialConnector/IOAuthSocialConnector at request time via IEnumerable<ISocialConnector>, and a
// wrong-platform resolution would surface as "the wrong platform's consent screen", not a DI error — a
// regression here would be silent without this.
public class CoreServiceResolutionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public CoreServiceResolutionTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void ConnectorSet_ResolvesOAuthConnector_PerPlatform()
    {
        using var scope = _factory.Services.CreateScope();
        var connectors = scope.ServiceProvider.GetRequiredService<IEnumerable<ISocialConnector>>().ToList();

        Assert.Equal(SocialPlatform.Threads,
            OAuthConnectorResolver.Require(connectors, SocialPlatform.Threads).Platform);
        Assert.Equal(SocialPlatform.Reddit,
            OAuthConnectorResolver.Require(connectors, SocialPlatform.Reddit).Platform);
    }

    [Fact]
    public void SocialOAuthOptions_Resolves_WithDefaultReturnUrlBase()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<SocialOAuthOptions>>().Value;

        Assert.Equal("/social", options.ReturnUrlBase);
    }
}
