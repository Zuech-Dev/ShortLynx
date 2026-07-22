using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class AdminUsersControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AdminUsersControllerTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task List_AsNonAdmin_Returns403()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(); // Owner, not super-admin
        var resp = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_Returns200()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var resp = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Add_ToExistingAccount_AddsMembership()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var accountId = await _factory.SeedAccountAsync("Target Co");
        var email = $"{Guid.NewGuid():N}@example.com";

        var resp = await client.PostAsJsonAsync("/admin/users",
            new AdminAddUserRequest(email, accountId, "Member"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var user = await resp.Content.ReadFromJsonAsync<AdminUserResponse>();
        Assert.Equal(email, user!.Email);
        Assert.Contains(user.Accounts, a => a.Id == accountId && a.Role == "Member");
    }

    [Fact]
    public async Task Add_NonexistentAccount_Returns404()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/admin/users",
            new AdminAddUserRequest($"{Guid.NewGuid():N}@example.com", Guid.CreateVersion7(), "Member"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Add_NoAccount_CreatesOwnedAccount()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var email = $"{Guid.NewGuid():N}@example.com";

        var resp = await client.PostAsJsonAsync("/admin/users", new AdminAddUserRequest(email));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var user = await resp.Content.ReadFromJsonAsync<AdminUserResponse>();
        Assert.Single(user!.Accounts);
        Assert.Equal("Owner", user.Accounts[0].Role);
    }

    [Fact]
    public async Task Deactivate_ThenList_ShowsInactive()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var email = $"{Guid.NewGuid():N}@example.com";
        var added = await (await client.PostAsJsonAsync("/admin/users", new AdminAddUserRequest(email)))
            .Content.ReadFromJsonAsync<AdminUserResponse>();

        var del = await client.DeleteAsync($"/admin/users/{added!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await client.GetFromJsonAsync<List<AdminUserResponse>>("/admin/users");
        Assert.Contains(list!, u => u.Id == added.Id && !u.IsActive);
    }

    [Fact]
    public async Task Edit_SuperAdminFlag_Toggles()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var added = await (await client.PostAsJsonAsync("/admin/users",
                new AdminAddUserRequest($"{Guid.NewGuid():N}@example.com")))
            .Content.ReadFromJsonAsync<AdminUserResponse>();

        var resp = await client.PutAsJsonAsync($"/admin/users/{added!.Id}",
            new AdminEditUserRequest(IsAdmin: true));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var list = await client.GetFromJsonAsync<List<AdminUserResponse>>("/admin/users");
        Assert.Contains(list!, u => u.Id == added.Id && u.IsAdmin);
    }

    [Fact]
    public async Task AssignAccount_ChangesRole()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var accountId = await _factory.SeedAccountAsync("Assign Co");
        var added = await (await client.PostAsJsonAsync("/admin/users",
                new AdminAddUserRequest($"{Guid.NewGuid():N}@example.com", accountId, "Viewer")))
            .Content.ReadFromJsonAsync<AdminUserResponse>();

        var resp = await client.PutAsJsonAsync($"/admin/users/{added!.Id}/accounts/{accountId}",
            new AdminAssignAccountRequest("Admin"));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var list = await client.GetFromJsonAsync<List<AdminUserResponse>>("/admin/users");
        var updated = list!.Single(u => u.Id == added.Id);
        Assert.Equal("Admin", updated.Accounts.Single(a => a.Id == accountId).Role);
    }

    [Fact]
    public async Task Deactivated_User_CannotSignIn()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        // Add a user with their own account so they could otherwise sign in, then deactivate them.
        var email = $"{Guid.NewGuid():N}@example.com";
        var added = await (await client.PostAsJsonAsync("/admin/users", new AdminAddUserRequest(email)))
            .Content.ReadFromJsonAsync<AdminUserResponse>();

        // Mint a token while the user is still active (magic links are only issued to active users),
        // then deactivate them and try to exchange the already-minted token.
        var token = await MintTokenAsync(email);
        await client.DeleteAsync($"/admin/users/{added!.Id}");

        var resp = await _factory.CreateClient()
            .PostAsJsonAsync("/auth/session", new CreateSessionRequest(token));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private async Task<string> MintTokenAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ShortLynx.Services.MagicLinks.IMagicLinkService>()
            .CreateTokenAsync(email);
    }
}
