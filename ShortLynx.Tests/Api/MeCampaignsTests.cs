using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

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
    public async Task CreateLink_WithCampaignId_AssignsAtCreation()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var campaign = await (await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("Launch")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com", CampaignId: campaign!.Id));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var fetched = await (await client.GetAsync($"/me/campaigns/{campaign.Id}"))
            .Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(1, fetched!.LinkCount);
    }

    [Fact]
    public async Task CreateLink_WithForeignCampaignId_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com", CampaignId: Guid.CreateVersion7()));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Campaigns_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Campaign analytics roll-up ────────────────────────────────────────────

    private static readonly DateTimeOffset Day1 = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 6, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Analytics_RollsUpClicksAcrossLinks_BothModes()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var campaign = await (await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("Launch")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        // Anonymous link in the campaign.
        var anon = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/a")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{anon!.Id}/campaign", new SetLinkCampaignRequest(campaign!.Id));

        // User-attributed link in the same campaign, with two recipient codes.
        var attr = await (await client.PostAsJsonAsync("/me/links",
                new CreateMyLinkRequest("https://example.com/b", "UserAttributed")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{attr!.Id}/campaign", new SetLinkCampaignRequest(campaign.Id));
        await client.PostAsJsonAsync($"/me/links/{attr.Id}/codes",
            new CreateUserCodesRequest([Guid.CreateVersion7(), Guid.CreateVersion7()]));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var scId = await db.ShortCodeEntities.Where(s => s.LinkId == anon.Id).Select(s => s.Id).FirstAsync();
            db.VisitEntities.AddRange(
                Visit(scId, "ip1", ClickSource.Twitter, DeviceType.Mobile, Day1),
                Visit(scId, "ip1", ClickSource.Twitter, DeviceType.Mobile, Day1),
                Visit(scId, "ip2", ClickSource.Direct, DeviceType.Desktop, Day2));

            var codeIds = await db.UserLinkCodeEntities.Where(c => c.LinkId == attr.Id).Select(c => c.Id).ToListAsync();
            db.UserVisitEntities.AddRange(
                UserVisit(codeIds[0], "ip3", ClickSource.Bluesky, DeviceType.Desktop, Day2),
                UserVisit(codeIds[1], "ip3", ClickSource.Bluesky, DeviceType.Tablet, Day2));
            await db.SaveChangesAsync();
        }

        var body = await (await client.GetAsync($"/me/campaigns/{campaign.Id}/analytics"))
            .Content.ReadFromJsonAsync<CampaignAnalyticsResponse>();

        Assert.Equal(2, body!.LinkCount);
        Assert.Equal(5, body.TotalClicks);   // 3 anonymous + 2 user-attributed
        Assert.Equal(3, body.UniqueClicks);  // ip1, ip2, ip3 deduped campaign-wide

        Assert.Equal(2, body.Links.Count);
        Assert.Equal(3, body.Links.Single(l => l.LinkId == anon.Id).TotalClicks);
        Assert.Equal(2, body.Links.Single(l => l.LinkId == anon.Id).UniqueClicks);
        Assert.Equal(2, body.Links.Single(l => l.LinkId == attr.Id).TotalClicks);

        Assert.Equal(5, body.Sources.Sum(s => s.Count));
        Assert.Equal(2, body.Sources.Single(s => s.Source == nameof(ClickSource.Twitter)).Count);
        Assert.Equal(2, body.Timeline.Count); // Day1, Day2
        Assert.Equal(Day1, body.FirstClickAt);
        Assert.Equal(Day2, body.LastClickAt);
    }

    [Fact]
    public async Task Analytics_EmptyCampaign_ReturnsZeroes()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var campaign = await (await client.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("Empty")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        var body = await (await client.GetAsync($"/me/campaigns/{campaign!.Id}/analytics"))
            .Content.ReadFromJsonAsync<CampaignAnalyticsResponse>();

        Assert.Equal(0, body!.LinkCount);
        Assert.Equal(0, body.TotalClicks);
        Assert.Empty(body.Links);
        Assert.Empty(body.Sources);
    }

    [Fact]
    public async Task Analytics_ForeignCampaign_Returns404()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();
        var campaign = await (await clientA.PostAsJsonAsync("/me/campaigns", new CreateCampaignRequest("A")))
            .Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.Equal(HttpStatusCode.NotFound,
            (await clientB.GetAsync($"/me/campaigns/{campaign!.Id}/analytics")).StatusCode);
    }

    private static VisitEntity Visit(Guid shortCodeId, string ip, ClickSource source, DeviceType device, DateTimeOffset at)
        => new()
        {
            Id = Guid.CreateVersion7(),
            ShortCodeId = shortCodeId,
            HashedIp = ip,
            Source = source,
            Device = device,
            ClickedAt = at,
        };

    private static UserVisitEntity UserVisit(Guid codeId, string ip, ClickSource source, DeviceType device, DateTimeOffset at)
        => new()
        {
            Id = Guid.CreateVersion7(),
            UserLinkCodeId = codeId,
            HashedIp = ip,
            Source = source,
            Device = device,
            ClickedAt = at,
        };
}
