using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Api;

public class TagsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public TagsControllerTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> CreateKeyClientAsync(string[]? scopes = null)
    {
        var accountId = await _factory.SeedAccountAsync();
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var granted = scopes ?? [Scopes.TagsRead, Scopes.TagsWrite, Scopes.LinksRead, Scopes.LinksWrite];
        var (_, plaintext) = await svc.CreateAsync("tag-key", granted, accountId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {plaintext}");
        return client;
    }

    [Fact]
    public async Task List_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutTagsReadScope_Returns403()
    {
        var client = await CreateKeyClientAsync([Scopes.LinksRead]);
        var response = await client.GetAsync("/tags");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutTagsWriteScope_Returns403()
    {
        var client = await CreateKeyClientAsync([Scopes.TagsRead]);
        var response = await client.PostAsJsonAsync("/tags", new CreateTagRequest("urgent"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ThenList_RoundTrips()
    {
        var client = await CreateKeyClientAsync();

        var created = await (await client.PostAsJsonAsync("/tags", new CreateTagRequest("api-created")))
            .Content.ReadFromJsonAsync<TagResponse>();
        Assert.NotNull(created);
        Assert.Equal("api-created", created!.Name);

        var list = await client.GetFromJsonAsync<List<TagResponse>>("/tags");
        Assert.Contains(list!, t => t.Name == "api-created");
    }

    [Fact]
    public async Task Create_Duplicate_Returns409()
    {
        var client = await CreateKeyClientAsync();
        await client.PostAsJsonAsync("/tags", new CreateTagRequest("dup"));

        var second = await client.PostAsJsonAsync("/tags", new CreateTagRequest("dup"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateLink_WithTagIds_AssignsTagsAtCreation()
    {
        var client = await CreateKeyClientAsync([Scopes.LinksWrite, Scopes.LinksRead, Scopes.TagsWrite]);
        var tag = await (await client.PostAsJsonAsync("/tags", new CreateTagRequest("provisioned")))
            .Content.ReadFromJsonAsync<TagResponse>();

        var resp = await client.PostAsJsonAsync("/links",
            new CreateLinkRequest("https://example.com", TagIds: [tag!.Id]));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var link = await resp.Content.ReadFromJsonAsync<LinkResponse>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynx.Data.Context.ShortLynxDbContext>();
        Assert.True(await db.LinkTagEntities.AnyAsync(lt => lt.LinkId == link!.Id && lt.TagId == tag.Id));
    }

    [Fact]
    public async Task CreateLink_WithTagIds_WithoutTagsWriteScope_Returns403()
    {
        var client = await CreateKeyClientAsync([Scopes.LinksWrite, Scopes.LinksRead]);

        var resp = await client.PostAsJsonAsync("/links",
            new CreateLinkRequest("https://example.com", TagIds: [Guid.CreateVersion7()]));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateLink_WithoutTagIds_SucceedsWithOnlyLinksWriteScope()
    {
        var client = await CreateKeyClientAsync([Scopes.LinksWrite, Scopes.LinksRead]);

        var resp = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }
}
