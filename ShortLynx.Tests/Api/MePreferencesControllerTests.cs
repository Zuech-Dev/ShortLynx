using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class MePreferencesControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MePreferencesControllerTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_NewSession_DefaultsToHamburger()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var prefs = await client.GetFromJsonAsync<UserPreferencesResponse>("/me/preferences");

        Assert.Equal("Hamburger", prefs!.NavStyle);
    }

    [Fact]
    public async Task Put_ThenGet_RoundTrips()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var putResp = await client.PutAsJsonAsync("/me/preferences", new UpdatePreferencesRequest("HorizontalScroll"));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var putBody = await putResp.Content.ReadFromJsonAsync<UserPreferencesResponse>();
        Assert.Equal("HorizontalScroll", putBody!.NavStyle);

        var prefs = await client.GetFromJsonAsync<UserPreferencesResponse>("/me/preferences");
        Assert.Equal("HorizontalScroll", prefs!.NavStyle);
    }

    [Fact]
    public async Task Put_UnknownStyle_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PutAsJsonAsync("/me/preferences", new UpdatePreferencesRequest("Sidebar"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/me/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
