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

public class MeTagsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeTagsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ThenGet_RoundTripsFields()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var created = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("urgent")))
            .Content.ReadFromJsonAsync<TagResponse>();

        Assert.NotNull(created);
        Assert.Equal("urgent", created!.Name);
        Assert.Equal(0, created.LinkCount);

        var fetched = await (await client.GetAsync($"/me/tags/{created.Id}")).Content.ReadFromJsonAsync<TagResponse>();
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("dup"));

        var resp = await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("dup"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task List_IsAccountScoped()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        await clientA.PostAsJsonAsync("/me/tags", new CreateTagRequest("a-only"));

        var listB = await (await clientB.GetAsync("/me/tags")).Content.ReadFromJsonAsync<List<TagResponse>>();
        Assert.DoesNotContain(listB!, t => t.Name == "a-only");
    }

    [Fact]
    public async Task Delete_RemovesTag()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("temp")))
            .Content.ReadFromJsonAsync<TagResponse>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/me/tags/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/me/tags/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task SetLinkTags_ReflectsInTagLinkCountAndLinkResponse()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var tag1 = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("one")))
            .Content.ReadFromJsonAsync<TagResponse>();
        var tag2 = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("two")))
            .Content.ReadFromJsonAsync<TagResponse>();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var assign = await client.PutAsJsonAsync($"/me/links/{link!.Id}/tags",
            new SetLinkTagsRequest([tag1!.Id, tag2!.Id]));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var fetchedTag1 = await (await client.GetAsync($"/me/tags/{tag1.Id}")).Content.ReadFromJsonAsync<TagResponse>();
        Assert.Equal(1, fetchedTag1!.LinkCount);

        var fetchedLink = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal([tag1.Id, tag2.Id], fetchedLink!.TagIds.OrderBy(id => id));

        // Full replace: dropping tag1 from the set removes just that association.
        await client.PutAsJsonAsync($"/me/links/{link.Id}/tags", new SetLinkTagsRequest([tag2.Id]));
        var afterTag1 = await (await client.GetAsync($"/me/tags/{tag1.Id}")).Content.ReadFromJsonAsync<TagResponse>();
        Assert.Equal(0, afterTag1!.LinkCount);
        var afterTag2 = await (await client.GetAsync($"/me/tags/{tag2.Id}")).Content.ReadFromJsonAsync<TagResponse>();
        Assert.Equal(1, afterTag2!.LinkCount);

        var afterLink = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal([tag2.Id], afterLink!.TagIds);
    }

    [Fact]
    public async Task LinkResponse_CarriesTagIds_SoAClientCanShowTheCurrentAssignment()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var tag = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("checked")))
            .Content.ReadFromJsonAsync<TagResponse>();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/tagged-link")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        // Untagged is an empty array, not null — a client can iterate it directly without a null check.
        Assert.Empty(link!.TagIds);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/me/links/{link.Id}/tags", new SetLinkTagsRequest([tag!.Id]))).StatusCode);

        // The assignment has to be READABLE, not just writable — same "PUT sets it, nothing could read
        // it back" gap CampaignId/FolderId/CustomDomainId were fixed for. Without this a tag multi-select
        // has no way to show which tags are already checked.
        var fetched = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal([tag.Id], fetched!.TagIds);

        // The batched list path (GET /me/links) must carry it too, not just the single-link GET.
        var listed = await (await client.GetAsync("/me/links")).Content.ReadFromJsonAsync<List<LinkResponse>>();
        Assert.Equal([tag.Id], Assert.Single(listed!, l => l.Id == link.Id).TagIds);

        // Clearing the set comes back as an empty array, not the stale previous ids.
        await client.PutAsJsonAsync($"/me/links/{link.Id}/tags", new SetLinkTagsRequest([]));
        var cleared = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Empty(cleared!.TagIds);
    }

    [Fact]
    public async Task SetLinkTags_ForeignTag_Returns400()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        var foreignTag = await (await clientB.PostAsJsonAsync("/me/tags", new CreateTagRequest("foreign")))
            .Content.ReadFromJsonAsync<TagResponse>();
        var link = await (await clientA.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await clientA.PutAsJsonAsync($"/me/links/{link!.Id}/tags", new SetLinkTagsRequest([foreignTag!.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Tags_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Nickname ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetNickname_ThenGet_RoundTrips()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Null(link!.Nickname);

        var set = await client.PutAsJsonAsync($"/me/links/{link.Id}/nickname", new SetLinkNicknameRequest("  My Link  "));
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var fetched = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal("My Link", fetched!.Nickname);

        // Clearing with null/blank works too.
        await client.PutAsJsonAsync($"/me/links/{link.Id}/nickname", new SetLinkNicknameRequest(null));
        var cleared = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Null(cleared!.Nickname);
    }

    [Fact]
    public async Task SetNickname_ForeignLink_Returns404()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();
        var link = await (await clientA.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await clientB.PutAsJsonAsync($"/me/links/{link!.Id}/nickname", new SetLinkNicknameRequest("hack"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Links list search/filter ────────────────────────────────────────────

    [Fact]
    public async Task ListLinks_SearchMatchesNicknameUrlOrTagName()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var tag = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("marketing")))
            .Content.ReadFromJsonAsync<TagResponse>();

        var byNickname = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/a")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{byNickname!.Id}/nickname", new SetLinkNicknameRequest("Spring Sale"));

        // The host must be a real, DNS-resolvable domain (UrlValidationService does a live SSRF-guard
        // lookup) -- keep it as example.com like every other link here and put the search term in the
        // path instead, matching this test file's own convention elsewhere.
        var byUrl = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/spring-sale-b")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var byTag = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/c")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{byTag!.Id}/tags", new SetLinkTagsRequest([tag!.Id]));

        var unrelated = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/d")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var results = await (await client.GetAsync("/me/links?search=spring"))
            .Content.ReadFromJsonAsync<List<LinkResponse>>();
        Assert.Contains(results!, l => l.Id == byNickname.Id);
        Assert.Contains(results!, l => l.Id == byUrl!.Id);
        Assert.DoesNotContain(results!, l => l.Id == unrelated!.Id);

        var byTagName = await (await client.GetAsync("/me/links?search=marketing"))
            .Content.ReadFromJsonAsync<List<LinkResponse>>();
        Assert.Contains(byTagName!, l => l.Id == byTag.Id);
        Assert.DoesNotContain(byTagName!, l => l.Id == unrelated!.Id);
    }

    [Fact]
    public async Task ListLinks_FiltersByTagIdAndFolderId()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var tag = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("filterme")))
            .Content.ReadFromJsonAsync<TagResponse>();
        var folder = await (await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("filterfolder")))
            .Content.ReadFromJsonAsync<FolderResponse>();

        var tagged = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/tagged")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{tagged!.Id}/tags", new SetLinkTagsRequest([tag!.Id]));

        var filed = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/filed")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{filed!.Id}/folder", new SetLinkFolderRequest(folder!.Id));

        var neither = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/neither")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var byTag = await (await client.GetAsync($"/me/links?tagId={tag.Id}")).Content.ReadFromJsonAsync<List<LinkResponse>>();
        Assert.Single(byTag!, l => l.Id == tagged.Id);
        Assert.DoesNotContain(byTag!, l => l.Id == neither!.Id);

        var byFolder = await (await client.GetAsync($"/me/links?folderId={folder.Id}")).Content.ReadFromJsonAsync<List<LinkResponse>>();
        Assert.Single(byFolder!, l => l.Id == filed.Id);
        Assert.DoesNotContain(byFolder!, l => l.Id == neither!.Id);
    }

    // ── Analytics rollup ────────────────────────────────────────────────────

    [Fact]
    public async Task Analytics_FanOut_LinkWithTwoTags_CountsTowardBoth()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var tagUrgent = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("urgent")))
            .Content.ReadFromJsonAsync<TagResponse>();
        var tagQ3 = await (await client.PostAsJsonAsync("/me/tags", new CreateTagRequest("q3")))
            .Content.ReadFromJsonAsync<TagResponse>();

        // One link carries BOTH tags -- its clicks should roll up into each tag's analytics
        // independently, unlike Folder's single-parent join.
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/both")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PutAsJsonAsync($"/me/links/{link!.Id}/tags", new SetLinkTagsRequest([tagUrgent!.Id, tagQ3!.Id]));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var sc = await db.ShortCodeEntities.Where(s => s.LinkId == link.Id).Select(s => s.Id).FirstAsync();
            db.VisitEntities.AddRange(Enumerable.Range(0, 3).Select(i => new VisitEntity
            {
                Id = Guid.CreateVersion7(), ShortCodeId = sc, HashedIp = $"ip{i}",
                Source = ClickSource.Direct, Device = DeviceType.Desktop, ClickedAt = DateTimeOffset.UtcNow,
            }));
            await db.SaveChangesAsync();
        }

        var urgentBody = await (await client.GetAsync($"/me/tags/{tagUrgent.Id}/analytics"))
            .Content.ReadFromJsonAsync<TagAnalyticsResponse>();
        var q3Body = await (await client.GetAsync($"/me/tags/{tagQ3.Id}/analytics"))
            .Content.ReadFromJsonAsync<TagAnalyticsResponse>();

        Assert.Equal(3, urgentBody!.TotalClicks);
        Assert.Equal(3, q3Body!.TotalClicks);
        Assert.Single(urgentBody.Links);
        Assert.Single(q3Body.Links);
    }

    [Fact]
    public async Task Analytics_ForeignTag_Returns404()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();
        var tag = await (await clientA.PostAsJsonAsync("/me/tags", new CreateTagRequest("a")))
            .Content.ReadFromJsonAsync<TagResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/me/tags/{tag!.Id}/analytics")).StatusCode);
    }
}
