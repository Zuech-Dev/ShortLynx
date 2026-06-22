using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class MeControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeControllerTests(ApiFactory factory) => _factory = factory;

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithoutSession_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsUserAndAccount()
    {
        var (client, userId, accountId) = await _factory.CreateSessionClientAsync();
        var body = await client.GetFromJsonAsync<UserSummary>("/me");
        Assert.Equal(userId, body!.Id);
        Assert.Equal(accountId, body.AccountId);
        Assert.Equal("Owner", body.Role);
    }

    [Fact]
    public async Task MeAccounts_ListsTheUsersAccounts()
    {
        var (client, _, accountId) = await _factory.CreateSessionClientAsync();
        var accounts = await client.GetFromJsonAsync<List<AccountResponse>>("/me/accounts");
        Assert.Contains(accounts!, a => a.Id == accountId && a.Role == "Owner");
    }

    // ── /me/links ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Links_CreateThenList_ScopedToAccount()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var created = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await created.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal(8, link!.ShortCode.Length);

        var list = await client.GetFromJsonAsync<List<LinkResponse>>("/me/links");
        Assert.Single(list!);
        Assert.Equal(link.Id, list![0].Id);
    }

    [Fact]
    public async Task Links_OfOneAccount_NotVisibleToAnother()
    {
        var (clientA, _, _) = await _factory.CreateSessionClientAsync();
        await clientA.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://a.example.com"));

        var (clientB, _, _) = await _factory.CreateSessionClientAsync();
        var listB = await clientB.GetFromJsonAsync<List<LinkResponse>>("/me/links");
        Assert.Empty(listB!);
    }

    [Fact]
    public async Task Links_CreateUserAttributed_HasNoShortCode()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com", "UserAttributed"));
        var link = await created.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal("UserAttributed", link!.Mode);
        Assert.Equal("", link.ShortCode);
    }

    [Fact]
    public async Task Links_InvalidUrl_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var created = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://192.168.1.1/"));
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    // ── /me/api-keys ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiKeys_Create_ReturnsPlaintext_ThenListsWithoutIt()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var created = await client.PostAsJsonAsync("/me/api-keys", new CreateMyApiKeyRequest("CI", ["links:read"]));
        var key = await created.Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.Equal(64, key!.PlaintextKey.Length);

        var list = await client.GetFromJsonAsync<List<MyApiKeyResponse>>("/me/api-keys");
        Assert.Single(list!);
        Assert.Equal("CI", list![0].Name);
        Assert.True(list[0].IsActive);
    }

    [Fact]
    public async Task ApiKeys_Revoke_Returns204()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var key = await (await client.PostAsJsonAsync("/me/api-keys", new CreateMyApiKeyRequest("k", ["links:read"])))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        var del = await client.DeleteAsync($"/me/api-keys/{key!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    // ── /me/domains ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Domains_Add_ReturnsPendingWithTxtInstructions()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var domain = $"d{Guid.NewGuid():N}.example.com";

        var created = await client.PostAsJsonAsync("/me/domains", new AddDomainRequest(domain));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<DomainResponse>();
        Assert.Equal("Pending", body!.Status);
        Assert.Equal($"_shortlynx-verify.{domain}", body.VerificationHost);
    }
}
