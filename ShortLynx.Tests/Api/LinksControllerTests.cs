using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Api;

public class LinksControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LinksControllerTests(ApiFactory factory) => _factory = factory;

    // Mints a real API key via the service layer and returns an HttpClient pre-configured with it.
    // All scopes are included by default; pass a subset to test scope enforcement.
    private async Task<(HttpClient Client, string PlaintextKey)> CreateAuthenticatedClientAsync(
        string[]? scopes = null)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var grantedScopes = scopes ?? [Scopes.LinksWrite, Scopes.LinksRead, Scopes.CodesWrite, Scopes.AnalyticsRead];
        var (_, plaintext) = await svc.CreateAsync("test-key", grantedScopes);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {plaintext}");
        return (client, plaintext);
    }

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task NoAuthHeader_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 0000000000000000000000000000000000000000000000000000000000000000");
        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST /links ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateLink_ValidUrl_Returns201WithBody()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal("https://example.com", body.Url);
        Assert.Equal(8, body.ShortCode.Length);
        Assert.Equal("Anonymous", body.Mode);
    }

    [Fact]
    public async Task CreateLink_Returns201WithLocationHeader()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/page"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateLink_InvalidUrl_Returns400()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        // SSRF private IP — UrlValidationService will reject it.
        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://192.168.1.1/"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_MissingUrl_Returns400()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/links", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GET /links/{id} ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetLink_OwnedLink_Returns200()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var created = await (await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var response = await client.GetAsync($"/links/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal(created.Id, body!.Id);
        Assert.Equal("https://example.com", body.Url);
    }

    [Fact]
    public async Task GetLink_AnotherKeysLink_Returns404()
    {
        var (clientA, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _) = await CreateAuthenticatedClientAsync();

        var created = await (await clientA.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        // Client B cannot see Client A's link.
        var response = await clientB.GetAsync($"/links/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLink_UnknownId_Returns404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/links/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── POST /links/{id}/codes ────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserCodes_Returns200WithOneCodPerUser()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var link = await (await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var userIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var response = await client.PostAsJsonAsync($"/links/{link!.Id}/codes",
            new CreateUserCodesRequest(userIds));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var codes = await response.Content.ReadFromJsonAsync<List<UserCodeResponse>>();
        Assert.Equal(2, codes!.Count);
        Assert.All(codes, c => Assert.Equal(8, c.Code.Length));
    }

    [Fact]
    public async Task CreateUserCodes_IsIdempotent_ReturnsSameCode()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var link = await (await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var userId = Guid.CreateVersion7();
        var request = new CreateUserCodesRequest([userId]);

        var first = (await (await client.PostAsJsonAsync($"/links/{link!.Id}/codes", request))
            .Content.ReadFromJsonAsync<List<UserCodeResponse>>())!.Single();

        var second = (await (await client.PostAsJsonAsync($"/links/{link.Id}/codes", request))
            .Content.ReadFromJsonAsync<List<UserCodeResponse>>())!.Single();

        Assert.Equal(first.Code, second.Code);
    }

    [Fact]
    public async Task CreateUserCodes_AnotherKeysLink_Returns404()
    {
        var (clientA, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _) = await CreateAuthenticatedClientAsync();

        var link = await (await clientA.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var response = await clientB.PostAsJsonAsync($"/links/{link!.Id}/codes",
            new CreateUserCodesRequest([Guid.CreateVersion7()]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /links/{id}/analytics ─────────────────────────────────────────────

    [Fact]
    public async Task GetAnalytics_NewLink_Returns200WithZeroClicks()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var link = await (await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var response = await client.GetAsync($"/links/{link!.Id}/analytics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkAnalyticsResponse>();
        Assert.Equal(0, body!.TotalClicks);
        Assert.Null(body.LastClickAt);
        Assert.Single(body.Codes); // one ShortCode
        Assert.Equal(0, body.Codes[0].ClickCount);
    }

    [Fact]
    public async Task GetAnalytics_AnotherKeysLink_Returns404()
    {
        var (clientA, _) = await CreateAuthenticatedClientAsync();
        var (clientB, _) = await CreateAuthenticatedClientAsync();

        var link = await (await clientA.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var response = await clientB.GetAsync($"/links/{link!.Id}/analytics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalytics_UnknownLink_Returns404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/links/{Guid.CreateVersion7()}/analytics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Scope enforcement ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateLink_MissingLinksWriteScope_Returns403()
    {
        var (client, _) = await CreateAuthenticatedClientAsync([Scopes.LinksRead]);
        var response = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListLinks_MissingLinksReadScope_Returns403()
    {
        var (client, _) = await CreateAuthenticatedClientAsync([Scopes.LinksWrite]);
        var response = await client.GetAsync("/links");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLink_MissingLinksReadScope_Returns403()
    {
        // Create with full scopes first so we have a link ID to target.
        var (fullClient, _) = await CreateAuthenticatedClientAsync();
        var link = await (await fullClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var (narrowClient, _) = await CreateAuthenticatedClientAsync([Scopes.LinksWrite]);
        var response = await narrowClient.GetAsync($"/links/{link!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUserCodes_MissingCodesWriteScope_Returns403()
    {
        var (fullClient, _) = await CreateAuthenticatedClientAsync();
        var link = await (await fullClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var (narrowClient, _) = await CreateAuthenticatedClientAsync([Scopes.LinksRead, Scopes.LinksWrite]);
        var response = await narrowClient.PostAsJsonAsync($"/links/{link!.Id}/codes",
            new CreateUserCodesRequest([Guid.CreateVersion7()]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalytics_MissingAnalyticsReadScope_Returns403()
    {
        var (fullClient, _) = await CreateAuthenticatedClientAsync();
        var link = await (await fullClient.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var (narrowClient, _) = await CreateAuthenticatedClientAsync([Scopes.LinksRead, Scopes.LinksWrite]);
        var response = await narrowClient.GetAsync($"/links/{link!.Id}/analytics");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
