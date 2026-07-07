using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShortLynx.Tests.Admin;

// Route + auth wiring for the Reddit OAuth endpoints, mirroring ThreadsOAuthRouteTests: full OAuth
// round-trips against live Reddit aren't practical in CI; the connector's exchange logic is covered at
// the unit level (RedditConnectorTests) — this guards that the routes exist and require a session.
public class RedditOAuthRouteTests : IClassFixture<AdminFactory>
{
    private readonly AdminFactory _factory;
    public RedditOAuthRouteTests(AdminFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task GetAuthorize_RouteResolves_AndRequiresAuth()
    {
        var resp = await Client().GetAsync("/social/reddit/authorize");

        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/auth/login", resp.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetCallback_RouteResolves_AndRequiresAuth()
    {
        var resp = await Client().GetAsync("/social/reddit/callback?code=x&state=y");

        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/auth/login", resp.Headers.Location?.OriginalString);
    }
}
