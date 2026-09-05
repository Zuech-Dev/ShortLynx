using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class MeFoldersTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeFoldersTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ThenGet_RoundTripsFields()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var created = await (await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("Client X")))
            .Content.ReadFromJsonAsync<FolderResponse>();

        Assert.NotNull(created);
        Assert.Equal("Client X", created!.Name);
        Assert.Equal(0, created.LinkCount);

        var fetched = await (await client.GetAsync($"/me/folders/{created.Id}"))
            .Content.ReadFromJsonAsync<FolderResponse>();
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_IsAccountScoped()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        await clientA.PostAsJsonAsync("/me/folders", new CreateFolderRequest("A-only"));

        var listB = await (await clientB.GetAsync("/me/folders"))
            .Content.ReadFromJsonAsync<List<FolderResponse>>();
        Assert.DoesNotContain(listB!, f => f.Name == "A-only");
    }

    [Fact]
    public async Task Get_ForeignFolder_Returns404()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        var created = await (await clientA.PostAsJsonAsync("/me/folders", new CreateFolderRequest("A1")))
            .Content.ReadFromJsonAsync<FolderResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/me/folders/{created!.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_RenamesFolder()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await (await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("Old")))
            .Content.ReadFromJsonAsync<FolderResponse>();

        var updated = await (await client.PutAsJsonAsync($"/me/folders/{created!.Id}", new UpdateFolderRequest("New")))
            .Content.ReadFromJsonAsync<FolderResponse>();

        Assert.Equal("New", updated!.Name);
    }

    [Fact]
    public async Task Delete_RemovesFolder()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await (await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("Temp")))
            .Content.ReadFromJsonAsync<FolderResponse>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/me/folders/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/me/folders/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task AssignLinkToFolder_ReflectsInLinkCount()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var folder = await (await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("Client X")))
            .Content.ReadFromJsonAsync<FolderResponse>();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var assign = await client.PutAsJsonAsync($"/me/links/{link!.Id}/folder", new SetLinkFolderRequest(folder!.Id));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var fetched = await (await client.GetAsync($"/me/folders/{folder.Id}")).Content.ReadFromJsonAsync<FolderResponse>();
        Assert.Equal(1, fetched!.LinkCount);

        var unassign = await client.PutAsJsonAsync($"/me/links/{link.Id}/folder", new SetLinkFolderRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, unassign.StatusCode);

        var after = await (await client.GetAsync($"/me/folders/{folder.Id}")).Content.ReadFromJsonAsync<FolderResponse>();
        Assert.Equal(0, after!.LinkCount);
    }

    [Fact]
    public async Task AssignLink_ToForeignFolder_Returns400()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        var foreignFolder = await (await clientB.PostAsJsonAsync("/me/folders", new CreateFolderRequest("B")))
            .Content.ReadFromJsonAsync<FolderResponse>();
        var link = await (await clientA.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await clientA.PutAsJsonAsync($"/me/links/{link!.Id}/folder", new SetLinkFolderRequest(foreignFolder!.Id));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Folders_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/folders");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task LinkResponse_CarriesFolderId_SoAClientCanShowTheCurrentAssignment()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var folder = await (await client.PostAsJsonAsync("/me/folders", new CreateFolderRequest("Filed")))
            .Content.ReadFromJsonAsync<FolderResponse>();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/filed")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        Assert.Null(link!.FolderId);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/me/links/{link.Id}/folder", new SetLinkFolderRequest(folder!.Id))).StatusCode);

        var fetched = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal(folder.Id, fetched!.FolderId);

        await client.PutAsJsonAsync($"/me/links/{link.Id}/folder", new SetLinkFolderRequest(null));
        var cleared = await (await client.GetAsync($"/me/links/{link.Id}")).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Null(cleared!.FolderId);
    }
}
