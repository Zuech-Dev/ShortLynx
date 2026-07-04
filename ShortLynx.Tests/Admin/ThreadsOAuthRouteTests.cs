using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShortLynx.Tests.Admin;

// Route + auth wiring for the two Threads OAuth endpoints Meta's app dashboard points at. Full OAuth
// round-trips against a live Meta sandbox aren't practical in CI; the connector's own exchange logic is
// covered at the unit level (ThreadsConnectorTests) — this guards that the routes exist and require a
// session, the same contract SwitchAccountRouteTests guards for the account switcher.
public class ThreadsOAuthRouteTests : IClassFixture<AdminFactory>
{
    private readonly AdminFactory _factory;
    public ThreadsOAuthRouteTests(AdminFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task GetAuthorize_RouteResolves_AndRequiresAuth()
    {
        var resp = await Client().GetAsync("/social/threads/authorize");

        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/auth/login", resp.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetCallback_RouteResolves_AndRequiresAuth()
    {
        var resp = await Client().GetAsync("/social/threads/callback?code=x&state=y");

        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/auth/login", resp.Headers.Location?.OriginalString);
    }
}
