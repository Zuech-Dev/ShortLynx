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

/// <summary>
/// POST /me/switch-account — re-issues the session for a different account the user belongs to. Added
/// alongside the account-switcher UI: GET /me/accounts already listed the user's other accounts ("for
/// account switching", per its own doc comment) but nothing could actually act on that list — the
/// account id is baked into the JWT/refresh-token claims at sign-in, not swappable client-side.
/// </summary>
public class MeSwitchAccountTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeSwitchAccountTests(ApiFactory factory) => _factory = factory;

    /// <summary>Adds an existing user as a member of a second, freshly-seeded account.</summary>
    private async Task<Guid> AddSecondMembershipAsync(Guid userId, AccountRole role, string accountName = "Second Co")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

        var account = new AccountEntity { Id = Guid.CreateVersion7(), Name = accountName, CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        db.Add(account);
        db.Add(new MembershipEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, UserAccountId = userId,
            Role = role, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task SwitchToAMembership_ReturnsTheNewAccountAndRole()
    {
        var (client, userId, _) = await _factory.CreateSessionClientAsync(AccountRole.Owner);
        var secondAccountId = await AddSecondMembershipAsync(userId, AccountRole.Member);

        var resp = await client.PostAsJsonAsync("/me/switch-account", new SwitchAccountRequest(secondAccountId));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(secondAccountId, body!.AccountId);
        Assert.Equal("Member", body.Role);
    }

    [Fact]
    public async Task SwitchToAMembership_IssuesCookiesThatAuthenticateTheNewAccount()
    {
        var (client, userId, firstAccountId) = await _factory.CreateSessionClientAsync(AccountRole.Owner);
        var secondAccountId = await AddSecondMembershipAsync(userId, AccountRole.Member);

        var switchResp = await client.PostAsJsonAsync("/me/switch-account", new SwitchAccountRequest(secondAccountId));
        var cookies = switchResp.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';', 2)[0])
            .Select(kv => kv.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);

        // A fresh client with only the new cookie — no Bearer header — proves the cookie itself
        // authenticates as the new account, not just that the response body claimed it did.
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Add("Cookie", $"sl_access={cookies["sl_access"]}");
        var meResp = await _factory.CreateClient().SendAsync(req);

        var me = await meResp.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(secondAccountId, me!.AccountId);
        Assert.NotEqual(firstAccountId, me.AccountId);
        Assert.Equal("Member", me.Role);
    }

    [Fact]
    public async Task SwitchToAnAccountNotAMember_Returns403()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var strangerAccountId = await _factory.SeedAccountAsync("Someone Else's Account");

        var resp = await client.PostAsJsonAsync("/me/switch-account", new SwitchAccountRequest(strangerAccountId));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient()
            .PostAsJsonAsync("/me/switch-account", new SwitchAccountRequest(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
