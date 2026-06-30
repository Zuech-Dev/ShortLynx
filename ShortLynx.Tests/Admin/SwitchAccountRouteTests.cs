using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShortLynx.Tests.Admin;

// Guards the wiring that the model/component unit tests can't see: the AccountSwitcher form posts to
// "/auth/switch", and the SwitchAccount page must actually answer at that path. The page name is
// SwitchAccount, so the conventional route would be /Auth/SwitchAccount — a POST to /auth/switch 404s
// unless the page pins `@page "/auth/switch"`. This regressed exactly that way once.
public class SwitchAccountRouteTests : IClassFixture<AdminFactory>
{
    private readonly AdminFactory _factory;
    public SwitchAccountRouteTests(AdminFactory factory) => _factory = factory;

    // https base address so UseHttpsRedirection is a no-op and the request reaches routing/auth.
    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task PostAuthSwitch_RouteResolves_AndRequiresAuth()
    {
        var resp = await Client().PostAsync("/auth/switch", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["accountId"] = Guid.NewGuid().ToString() }));

        // The route exists (not 404). Unauthenticated, [Authorize] redirects to the login path —
        // proving the endpoint is wired and protected, not silently missing.
        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/auth/login", resp.Headers.Location?.OriginalString);
    }
}
