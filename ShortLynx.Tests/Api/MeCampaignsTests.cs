using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class MeCampaignsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeCampaignsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ThenGet_RoundTripsFields()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var created = await (await client.PostAsJsonAsync("/me/campaigns",
                new CreateCampaignRequest("Spring Launch", "Q2 push", "newsletter", "email", "spring")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.NotNull(created);
        Assert.Equal("Spring Launch", created!.Name);
        Assert.Equal("newsletter", created.UtmSource);
        Assert.Equal(0, created.LinkCount);

        var fetched = await (await client.GetAsync($"/me/campaigns/{created.Id}"))
            .Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("Q2 push", fetched.Description);
    }

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_IsAccountScoped()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        await clientA.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("A-only"));

        var listB = await (await clientB.GetAsync("/me/campaigns"))
            .Content.ReadFromJsonAsync<List<CampaignResponse>>();
        Assert.DoesNotContain(listB!, c => c.Name == "A-only");
    }

    [Fact]
    public async Task Get_ForeignCampaign_Returns404()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        var created = await (await clientA.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("A1")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/me/campaigns/{created!.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await (await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("Old", "d")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        var updated = await (await client.PutAsJsonAsync($"/me/campaigns/{created!.Id}",
                new UpdateCampaignRequest("New")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.Equal("New", updated!.Name);
        Assert.Null(updated.Description); // cleared on replace
    }

    [Fact]
    public async Task Delete_RemovesCampaign()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await (await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("Temp")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/me/campaigns/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/me/campaigns/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task AssignLinkToCampaign_ReflectsInLinkCount()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var campaign = await (await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("Launch")))
            .Content.ReadFromJsonAsync<CampaignResponse>();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var assign = await client.PutAsJsonAsync($"/me/links/{link!.Id}/campaign",
            new SetLinkCampaignRequest(campaign!.Id));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var fetched = await (await client.GetAsync($"/me/campaigns/{campaign.Id}"))
            .Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(1, fetched!.LinkCount);

        // Unassign with null.
        var unassign = await client.PutAsJsonAsync($"/me/links/{link.Id}/campaign",
            new SetLinkCampaignRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, unassign.StatusCode);

        var after = await (await client.GetAsync($"/me/campaigns/{campaign.Id}"))
            .Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(0, after!.LinkCount);
    }

    [Fact]
    public async Task AssignLink_ToForeignCampaign_Returns400()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        var foreignCampaign = await (await clientB.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("B")))
            .Content.ReadFromJsonAsync<CampaignResponse>();
        var link = await (await clientA.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await clientA.PutAsJsonAsync($"/me/links/{link!.Id}/campaign",
            new SetLinkCampaignRequest(foreignCampaign!.Id));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Campaigns_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
