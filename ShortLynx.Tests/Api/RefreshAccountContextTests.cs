using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Auth;

namespace ShortLynx.Tests.Api;

/// <summary>
/// /auth/refresh must preserve the account the session was acting in — not snap the user back to
/// their primary (highest-role) account — for as long as the membership still holds. The refresh
/// token records the acting account at issuance; membership is re-validated on every refresh.
/// </summary>
public class RefreshAccountContextTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public RefreshAccountContextTests(ApiFactory factory) => _factory = factory;

    /// <summary>User who owns account A (primary) and is a Member of account B (secondary).</summary>
    private async Task<(UserAccountEntity User, Guid PrimaryId, Guid SecondaryId)> SeedTwoAccountUserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

        var user = new UserAccountEntity
        {
            Id = Guid.CreateVersion7(), Email = $"{Guid.NewGuid():N}@example.com",
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        var primary = new AccountEntity { Id = Guid.CreateVersion7(), Name = "Primary", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var secondary = new AccountEntity { Id = Guid.CreateVersion7(), Name = "Secondary", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        db.AddRange(user, primary, secondary,
            new MembershipEntity { Id = Guid.CreateVersion7(), AccountId = primary.Id, UserAccountId = user.Id, Role = AccountRole.Owner, CreatedAt = DateTimeOffset.UtcNow },
            new MembershipEntity { Id = Guid.CreateVersion7(), AccountId = secondary.Id, UserAccountId = user.Id, Role = AccountRole.Member, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (user, primary.Id, secondary.Id);
    }

    private async Task<SessionTokens> IssueAsync(UserAccountEntity user, Guid? accountId, AccountRole? role)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserSessionService>()
            .IssueAsync(user, accountId, role);
    }

    /// <summary>Refresh via the endpoint, then read /me with the new access token.</summary>
    private async Task<UserSummary> RefreshAndGetMeAsync(string refreshToken)
    {
        var client = _factory.CreateClient();
        var refreshed = await (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken }))
            .Content.ReadFromJsonAsync<RefreshResponse>();

        var me = _factory.CreateClient();
        me.DefaultRequestHeaders.Add("Authorization", $"Bearer {refreshed!.AccessToken}");
        return (await me.GetFromJsonAsync<UserSummary>("/me"))!;
    }

    [Fact]
    public async Task Refresh_PreservesActingAccount_NotPrimary()
    {
        var (user, _, secondaryId) = await SeedTwoAccountUserAsync();
        var tokens = await IssueAsync(user, secondaryId, AccountRole.Member);

        var me = await RefreshAndGetMeAsync(tokens.RefreshToken);

        Assert.Equal(secondaryId, me.AccountId);
        Assert.Equal("Member", me.Role);
    }

    [Fact]
    public async Task Refresh_SurvivesMultipleRotations_InTheSameAccount()
    {
        var (user, _, secondaryId) = await SeedTwoAccountUserAsync();
        var tokens = await IssueAsync(user, secondaryId, AccountRole.Member);

        // The rotated token must carry the acting account forward, not just the first refresh.
        var client = _factory.CreateClient();
        var first = await (await client.PostAsJsonAsync("/auth/refresh", new { tokens.RefreshToken }))
            .Content.ReadFromJsonAsync<RefreshResponse>();
        var me = await RefreshAndGetMeAsync(first!.RefreshToken);

        Assert.Equal(secondaryId, me.AccountId);
    }

    [Fact]
    public async Task Refresh_RevokedMembership_FallsBackToPrimary()
    {
        var (user, primaryId, secondaryId) = await SeedTwoAccountUserAsync();
        var tokens = await IssueAsync(user, secondaryId, AccountRole.Member);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            await db.MembershipEntities
                .Where(m => m.AccountId == secondaryId && m.UserAccountId == user.Id)
                .ExecuteDeleteAsync();
        }

        var me = await RefreshAndGetMeAsync(tokens.RefreshToken);

        // The stale acting account must not survive the membership's revocation.
        Assert.Equal(primaryId, me.AccountId);
        Assert.Equal("Owner", me.Role);
    }

    [Fact]
    public async Task Refresh_LegacyTokenWithoutAccount_UsesPrimary()
    {
        var (user, primaryId, _) = await SeedTwoAccountUserAsync();
        // Pre-migration tokens have no stored account — the old snap-to-primary behavior is the
        // correct fallback for them.
        var tokens = await IssueAsync(user, accountId: null, role: null);

        var me = await RefreshAndGetMeAsync(tokens.RefreshToken);

        Assert.Equal(primaryId, me.AccountId);
    }
}
