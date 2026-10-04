using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Api;

public class CampaignsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CampaignsControllerTests(ApiFactory factory) => _factory = factory;

    // Mints a real API key via the service layer and returns an HttpClient pre-configured with it,
    // plus the account id it acts on (needed to seed account-scoped campaign fixtures).
    private async Task<(HttpClient Client, Guid AccountId)> CreateAuthenticatedClientAsync(string[]? scopes = null)
    {
        var accountId = await _factory.SeedAccountAsync();
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var granted = scopes ?? [Scopes.CampaignsRead, Scopes.LinksWrite];
        var (_, plaintext) = await svc.CreateAsync("camp-key", granted, accountId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {plaintext}");
        return (client, accountId);
    }

    private async Task<Guid> SeedCampaignAsync(Guid accountId, string name = "Launch")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        var campaign = new CampaignEntity
        {
            Id = Guid.CreateVersion7(), AccountId = accountId, Name = name, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CampaignEntities.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    // ── Auth & scoping ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListCampaigns_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListCampaigns_MissingCampaignsReadScope_Returns403()
    {
        var (client, _) = await CreateAuthenticatedClientAsync([Scopes.LinksWrite]);
        var response = await client.GetAsync("/campaigns");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCampaign_MissingCampaignsReadScope_Returns403()
    {
        var (client, _) = await CreateAuthenticatedClientAsync([Scopes.LinksWrite]);
        var response = await client.GetAsync($"/campaigns/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /campaigns ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListCampaigns_ReturnsAccountsCampaigns()
    {
        var (client, accountId) = await CreateAuthenticatedClientAsync();
        await SeedCampaignAsync(accountId, "Spring Launch");

        var response = await client.GetAsync("/campaigns");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<CampaignResponse>>();
        Assert.Contains(body!, c => c.Name == "Spring Launch");
    }

    [Fact]
    public async Task ListCampaigns_ExcludesAnotherAccountsCampaigns()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var otherAccountId = await _factory.SeedAccountAsync();
        await SeedCampaignAsync(otherAccountId, "Not Mine");

        var response = await client.GetAsync("/campaigns");

        var body = await response.Content.ReadFromJsonAsync<List<CampaignResponse>>();
        Assert.DoesNotContain(body!, c => c.Name == "Not Mine");
    }

    [Fact]
    public async Task ListCampaigns_ReportsLinkCount()
    {
        var (client, accountId) = await CreateAuthenticatedClientAsync();
        var campaignId = await SeedCampaignAsync(accountId);

        var created = await client.PostAsJsonAsync("/links",
            new CreateLinkRequest("https://example.com", CampaignId: campaignId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await (await client.GetAsync("/campaigns")).Content.ReadFromJsonAsync<List<CampaignResponse>>();
        Assert.Equal(1, body!.Single(c => c.Id == campaignId).LinkCount);
    }

    // ── GET /campaigns/{id} ────────────────────────────────────────────────────

    [Fact]
    public async Task GetCampaign_OwnedCampaign_Returns200()
    {
        var (client, accountId) = await CreateAuthenticatedClientAsync();
        var campaignId = await SeedCampaignAsync(accountId, "Q3 Push");

        var response = await client.GetAsync($"/campaigns/{campaignId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal("Q3 Push", body!.Name);
    }

    [Fact]
    public async Task GetCampaign_AnotherAccountsCampaign_Returns404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var otherAccountId = await _factory.SeedAccountAsync();
        var otherCampaignId = await SeedCampaignAsync(otherAccountId);

        var response = await client.GetAsync($"/campaigns/{otherCampaignId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCampaign_UnknownId_Returns404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/campaigns/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
