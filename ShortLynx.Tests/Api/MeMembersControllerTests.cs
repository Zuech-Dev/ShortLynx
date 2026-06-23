using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShortLynx.Core.Models.Requests;

namespace ShortLynx.Tests.Api;

public class MeMembersControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeMembersControllerTests(ApiFactory factory) => _factory = factory;

    private static async Task<Guid> InviteAndReadIdAsync(HttpClient client, string email, string role)
    {
        var resp = await client.PostAsJsonAsync("/me/members", new InviteMemberRequest(email, role));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("userAccountId").GetGuid();
    }

    [Fact]
    public async Task Invite_AsOwner_AddsMemberToAccount()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var email = $"{Guid.NewGuid():N}@example.com";

        await InviteAndReadIdAsync(client, email, "Member");

        var members = await client.GetFromJsonAsync<List<JsonElement>>("/me/members");
        Assert.Contains(members!, m => m.GetProperty("email").GetString() == email
            && m.GetProperty("role").GetString() == "Member");
    }

    [Fact]
    public async Task Invite_GrantingOwnRole_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        // An Owner may only grant roles strictly below Owner.
        var resp = await client.PostAsJsonAsync("/me/members",
            new InviteMemberRequest($"{Guid.NewGuid():N}@example.com", "Owner"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Invite_UnknownRole_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/members",
            new InviteMemberRequest($"{Guid.NewGuid():N}@example.com", "Wizard"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_AsOwner_Succeeds()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var memberId = await InviteAndReadIdAsync(client, $"{Guid.NewGuid():N}@example.com", "Viewer");

        var resp = await client.PutAsJsonAsync($"/me/members/{memberId}", new ChangeMemberRoleRequest("Member"));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task Remove_AsOwner_Succeeds()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var memberId = await InviteAndReadIdAsync(client, $"{Guid.NewGuid():N}@example.com", "Member");

        var resp = await client.DeleteAsync($"/me/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var members = await client.GetFromJsonAsync<List<JsonElement>>("/me/members");
        Assert.DoesNotContain(members!, m => m.GetProperty("userAccountId").GetGuid() == memberId);
    }

    [Fact]
    public async Task Remove_Self_Returns403()
    {
        var (client, userId, _) = await _factory.CreateSessionClientAsync();
        // An Owner can't remove themselves (they don't outrank an equal-role target).
        var resp = await client.DeleteAsync($"/me/members/{userId}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Members_Write_RequiresSession()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/me/members",
            new InviteMemberRequest("x@example.com", "Member"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
