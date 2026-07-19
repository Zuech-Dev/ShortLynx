using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Enums;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Api;

/// <summary>
/// Verifies that <c>AccountPermissions.ManageResources</c> (Member+) is enforced on every
/// <c>/me/*</c> write endpoint — a Viewer must be read-only, and in particular must not be able to
/// mint an API key (which would act role-blind with whatever scopes it was given).
/// </summary>
public class RoleEnforcementTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public RoleEnforcementTests(ApiFactory factory) => _factory = factory;

    // ── Viewer: reads allowed ─────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_CanListLinks()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Viewer);
        var response = await client.GetAsync("/me/links");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanListApiKeys()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Viewer);
        var response = await client.GetAsync("/me/api-keys");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Viewer: writes forbidden ──────────────────────────────────────────────

    [Theory]
    [InlineData("/me/links")]
    [InlineData("/me/domains")]
    [InlineData("/me/campaigns")]
    [InlineData("/me/api-keys")]
    public async Task Viewer_CannotCreateResources(string path)
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Viewer);
        // Deliberately invalid/empty body: the role gate runs before model validation (Order -3000),
        // so an under-privileged caller must get 403 even when the body wouldn't have parsed.
        var response = await client.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotMintApiKey_TheEscalationPath()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Viewer);
        var response = await client.PostAsJsonAsync("/me/api-keys",
            new CreateMyApiKeyRequest("escalate", [Scopes.LinksWrite, Scopes.DomainsWrite]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotRevokeApiKey()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Viewer);
        var response = await client.DeleteAsync($"/me/api-keys/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotModifyLinkSubresources()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Viewer);
        var id = Guid.CreateVersion7();

        var codes = await client.PostAsJsonAsync($"/me/links/{id}/codes", new { userIds = new[] { Guid.CreateVersion7() } });
        var domain = await client.PutAsJsonAsync($"/me/links/{id}/domain", new { customDomainId = (Guid?)null });
        var campaign = await client.PutAsJsonAsync($"/me/links/{id}/campaign", new { campaignId = (Guid?)null });
        var publish = await client.PostAsJsonAsync($"/me/links/{id}/publish", new { connectionIds = new[] { Guid.CreateVersion7() }, text = "x" });
        var refresh = await client.PostAsync($"/me/links/{id}/posts/refresh", null);

        // 403 (not 404) even for nonexistent ids: the role gate runs before model validation and
        // before any DB lookup, so a Viewer can't probe which resource ids exist or which bodies parse.
        Assert.All(new[] { codes, domain, campaign, publish, refresh },
            r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    // ── Member: writes allowed (the gate must not over-block) ─────────────────

    [Fact]
    public async Task Member_CanCreateLink()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);
        var response = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/m"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Member_CanMintApiKey()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);
        var response = await client.PostAsJsonAsync("/me/api-keys",
            new CreateMyApiKeyRequest("ok", [Scopes.LinksRead]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Role comes from the DB, not the token ─────────────────────────────────

    [Fact]
    public async Task DemotedMember_LosesWriteAccess_BeforeTokenExpiry()
    {
        var (client, userId, accountId) = await _factory.CreateSessionClientAsync(AccountRole.Member);

        // Demote directly in the DB — the JWT still carries role=Member.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynx.Data.Context.ShortLynxDbContext>();
            var membership = db.MembershipEntities.Single(m => m.AccountId == accountId && m.UserAccountId == userId);
            membership.Role = AccountRole.Viewer;
            db.SaveChanges();
        }

        var response = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/d"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemovedMember_LosesAllWriteAccess_BeforeTokenExpiry()
    {
        var (client, userId, accountId) = await _factory.CreateSessionClientAsync(AccountRole.Member);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynx.Data.Context.ShortLynxDbContext>();
            db.MembershipEntities.RemoveRange(
                db.MembershipEntities.Where(m => m.AccountId == accountId && m.UserAccountId == userId));
            db.SaveChanges();
        }

        var response = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/r"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Scope validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ApiKey_UnknownScopes_Rejected()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);
        var response = await client.PostAsJsonAsync("/me/api-keys",
            new CreateMyApiKeyRequest("bad", ["links:write", "admin:everything"]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin:everything", body);
    }

    [Fact]
    public async Task ApiKey_EmptyScopes_Rejected()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);
        var response = await client.PostAsJsonAsync("/me/api-keys", new CreateMyApiKeyRequest("none", []));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_DuplicateScopes_Deduplicated()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);
        var response = await client.PostAsJsonAsync("/me/api-keys",
            new CreateMyApiKeyRequest("dupes", [Scopes.LinksRead, Scopes.LinksRead]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = await response.Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.Equal([Scopes.LinksRead], key!.Scopes);
    }

    [Fact]
    public async Task ApiKey_KnownScopes_Accepted()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);
        var response = await client.PostAsJsonAsync("/me/api-keys",
            new CreateMyApiKeyRequest("good", Scopes.All));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = await response.Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.Equal(Scopes.All.Length, key!.Scopes.Length);
    }
}
