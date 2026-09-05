using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class AdminAccountsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AdminAccountsControllerTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task List_AsNonAdmin_Returns403()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(); // Owner, not super-admin
        var resp = await client.GetAsync("/admin/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_IncludesSeededAccount()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var accountId = await _factory.SeedAccountAsync("List Me Co");

        var list = await client.GetFromJsonAsync<List<AdminAccountSummaryResponse>>("/admin/accounts");
        Assert.Contains(list!, a => a.Id == accountId && a.Name == "List Me Co");
    }

    [Fact]
    public async Task Get_NonexistentAccount_Returns404()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var resp = await client.GetAsync($"/admin/accounts/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Update_RenamesAnyAccount_WithoutActorBeingAMember()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var accountId = await _factory.SeedAccountAsync("Original Name");

        var resp = await client.PutAsJsonAsync($"/admin/accounts/{accountId}",
            new UpdateAccountRequest("Renamed Co", null, null));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var updated = await resp.Content.ReadFromJsonAsync<AccountSettingsResponse>();
        Assert.Equal("Renamed Co", updated!.Name);

        var fetched = await client.GetFromJsonAsync<AccountSettingsResponse>($"/admin/accounts/{accountId}");
        Assert.Equal("Renamed Co", fetched!.Name);
    }

    [Fact]
    public async Task Update_PrivacyUrlWithoutConfirmation_Returns400()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var accountId = await _factory.SeedAccountAsync("Needs Confirm Co");

        var resp = await client.PutAsJsonAsync($"/admin/accounts/{accountId}",
            new UpdateAccountRequest("Needs Confirm Co", "https://example.com/privacy", null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_EnableCityAggregatesWithPrivacyPolicy_Succeeds()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var accountId = await _factory.SeedAccountAsync("City Co");

        var resp = await client.PutAsJsonAsync($"/admin/accounts/{accountId}",
            new UpdateAccountRequest("City Co", "https://example.com/privacy", null,
                ConfirmsDisclosure: true, EnableCityAggregates: true));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var updated = await resp.Content.ReadFromJsonAsync<AccountSettingsResponse>();
        Assert.True(updated!.EnableCityAggregates);
    }

    [Fact]
    public async Task Update_NonexistentAccount_Returns404()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var resp = await client.PutAsJsonAsync($"/admin/accounts/{Guid.CreateVersion7()}",
            new UpdateAccountRequest("Doesn't Matter", null, null));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
