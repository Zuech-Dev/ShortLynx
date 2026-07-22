using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Tests.Api;

public class AuthControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthControllerTests(ApiFactory factory) => _factory = factory;

    // Creates a user (via the magic-link service) who is an Owner of a fresh account, and returns a
    // valid magic-link token plus the ids — so /auth/session's gate passes via membership.
    private async Task<(string Token, Guid UserId, Guid AccountId)> SeedTokenForMemberAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

        // Magic links are only issued to existing active users, so provision the account first.
        var user = new UserAccountEntity
        {
            Id = Guid.CreateVersion7(), Email = email, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        db.UserAccountEntities.Add(user);
        await db.SaveChangesAsync();

        var magic = scope.ServiceProvider.GetRequiredService<IMagicLinkService>();
        var token = await magic.CreateTokenAsync(email);

        var account = new AccountEntity { Id = Guid.CreateVersion7(), Name = "Acme", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        db.AccountEntities.Add(account);
        db.MembershipEntities.Add(new MembershipEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, UserAccountId = user.Id,
            Role = AccountRole.Owner, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (token, user.Id, account.Id);
    }

    [Fact]
    public async Task Session_WithValidTokenAndMembership_ReturnsTokensAndUser()
    {
        var (token, userId, accountId) = await SeedTokenForMemberAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/session", new CreateSessionRequest(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.AccessToken);
        Assert.NotEmpty(body.RefreshToken);
        Assert.Equal(userId, body.User.Id);
        Assert.Equal(accountId, body.User.AccountId);
        Assert.Equal("Owner", body.User.Role);
    }

    [Fact]
    public async Task Session_WithInvalidToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/session", new CreateSessionRequest("not-a-real-token"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Session_UnlistedNonMember_Returns403()
    {
        // A user with a valid token but no membership and not on the allowlist.
        var email = $"{Guid.NewGuid():N}@example.com";
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            // Active user, but with no membership and not on the allowlist.
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            db.UserAccountEntities.Add(new UserAccountEntity
            {
                Id = Guid.CreateVersion7(), Email = email, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            });
            await db.SaveChangesAsync();
            token = await scope.ServiceProvider.GetRequiredService<IMagicLinkService>().CreateTokenAsync(email);
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/session", new CreateSessionRequest(token));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithAccessToken_ReturnsCurrentUser()
    {
        var (token, userId, _) = await SeedTokenForMemberAsync();
        var client = _factory.CreateClient();
        var session = await (await client.PostAsJsonAsync("/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();

        var me = _factory.CreateClient();
        me.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");
        var response = await me.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(userId, body!.Id);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesAndOldTokenStopsWorking()
    {
        var (token, _, _) = await SeedTokenForMemberAsync();
        var client = _factory.CreateClient();
        var session = await (await client.PostAsJsonAsync("/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();

        var refreshed = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(session!.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var refreshBody = await refreshed.Content.ReadFromJsonAsync<RefreshResponse>();
        Assert.NotEqual(session.RefreshToken, refreshBody!.RefreshToken);

        // Old refresh token no longer works.
        var reuse = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (token, _, _) = await SeedTokenForMemberAsync();
        var client = _factory.CreateClient();
        var session = await (await client.PostAsJsonAsync("/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();

        var logout = await client.PostAsJsonAsync("/auth/logout", new RefreshRequest(session!.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
