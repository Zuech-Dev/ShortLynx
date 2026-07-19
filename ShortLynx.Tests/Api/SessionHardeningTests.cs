using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Auth;

namespace ShortLynx.Tests.Api;

/// <summary>
/// Session-lifecycle hardening: removing a member revokes their refresh tokens for that account
/// (and only that account), and /auth/refresh is rate-limited so token stuffing isn't free.
/// </summary>
public class SessionHardeningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public SessionHardeningTests(ApiFactory factory) => _factory = factory;

    /// <summary>Owner of the account plus a Member target, each with a live session in it.</summary>
    private async Task<(Guid AccountId, UserAccountEntity Owner, UserAccountEntity Target)> SeedAccountWithMemberAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

        var owner = new UserAccountEntity { Id = Guid.CreateVersion7(), Email = $"{Guid.NewGuid():N}@example.com", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var target = new UserAccountEntity { Id = Guid.CreateVersion7(), Email = $"{Guid.NewGuid():N}@example.com", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var account = new AccountEntity { Id = Guid.CreateVersion7(), Name = "Team", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        db.AddRange(owner, target, account,
            new MembershipEntity { Id = Guid.CreateVersion7(), AccountId = account.Id, UserAccountId = owner.Id, Role = AccountRole.Owner, CreatedAt = DateTimeOffset.UtcNow },
            new MembershipEntity { Id = Guid.CreateVersion7(), AccountId = account.Id, UserAccountId = target.Id, Role = AccountRole.Member, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (account.Id, owner, target);
    }

    private async Task<SessionTokens> IssueAsync(UserAccountEntity user, Guid accountId, AccountRole role)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserSessionService>()
            .IssueAsync(user, accountId, role);
    }

    private async Task<HttpStatusCode> TryRefreshAsync(string refreshToken)
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/auth/refresh", new { refreshToken });
        return resp.StatusCode;
    }

    [Fact]
    public async Task RemoveMember_RevokesTheirRefreshTokens_ForThatAccount()
    {
        var (accountId, owner, target) = await SeedAccountWithMemberAsync();
        var targetTokens = await IssueAsync(target, accountId, AccountRole.Member);

        using (var scope = _factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            Assert.True(await accounts.RemoveMemberAsync(accountId, target.Id, owner.Id));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await TryRefreshAsync(targetTokens.RefreshToken));
    }

    [Fact]
    public async Task RemoveMember_LeavesOtherAccountsSessionsAlive()
    {
        var (accountId, owner, target) = await SeedAccountWithMemberAsync();

        // The target also owns a second, unrelated account with its own session.
        Guid personalId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var personal = new AccountEntity { Id = Guid.CreateVersion7(), Name = "Personal", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
            db.AddRange(personal, new MembershipEntity
            {
                Id = Guid.CreateVersion7(), AccountId = personal.Id, UserAccountId = target.Id,
                Role = AccountRole.Owner, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            personalId = personal.Id;
        }
        var teamTokens = await IssueAsync(target, accountId, AccountRole.Member);
        var personalTokens = await IssueAsync(target, personalId, AccountRole.Owner);

        using (var scope = _factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            Assert.True(await accounts.RemoveMemberAsync(accountId, target.Id, owner.Id));
        }

        // Only the removed account's session dies — the eviction must not be a global logout.
        Assert.Equal(HttpStatusCode.Unauthorized, await TryRefreshAsync(teamTokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, await TryRefreshAsync(personalTokens.RefreshToken));
    }

    [Fact]
    public async Task RemoveMember_DoesNotRevokeOtherMembersTokens()
    {
        var (accountId, owner, target) = await SeedAccountWithMemberAsync();
        var ownerTokens = await IssueAsync(owner, accountId, AccountRole.Owner);

        using (var scope = _factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            Assert.True(await accounts.RemoveMemberAsync(accountId, target.Id, owner.Id));
        }

        Assert.Equal(HttpStatusCode.OK, await TryRefreshAsync(ownerTokens.RefreshToken));
    }

    [Fact]
    public async Task ReplayingRotatedToken_StillRevokesAllSessions()
    {
        // The theft signal (a token that was already exchanged) must keep nuking everything —
        // only administrative revocations (logout, removal) are exempt from reuse detection.
        var (accountId, _, target) = await SeedAccountWithMemberAsync();
        var original = await IssueAsync(target, accountId, AccountRole.Member);

        Assert.Equal(HttpStatusCode.OK, await TryRefreshAsync(original.RefreshToken));          // rotate
        Assert.Equal(HttpStatusCode.Unauthorized, await TryRefreshAsync(original.RefreshToken)); // replay → nuke

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        Assert.DoesNotContain(await db.RefreshTokenEntities
            .Where(t => t.UserAccountId == target.Id).ToListAsync(), t => t.RevokedAt == null);
    }

    [Fact]
    public async Task Refresh_ExceedingIpLimit_Returns429()
    {
        var client = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:RefreshPermitLimit"] = "3",
                    ["RateLimit:RefreshWindowSeconds"] = "300",
                }));
        }).CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var resp = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = $"bogus-{i}" });
            statuses.Add(resp.StatusCode);
        }

        // 3 limited attempts get through (as 401s — bogus tokens), the rest are throttled.
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
