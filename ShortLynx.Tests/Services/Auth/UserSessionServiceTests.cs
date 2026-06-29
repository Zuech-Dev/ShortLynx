using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Auth;

namespace ShortLynx.Tests.Services.Auth;

public class UserSessionServiceTests
{
    private sealed class StubAccountService(AccountSummary? primary) : IAccountService
    {
        public Task<IReadOnlyList<AccountSummary>> ListAccountsForUserAsync(Guid userAccountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountSummary>>(primary is null ? [] : [primary]);

        public Task<AccountEntity> CreateAccountWithOwnerAsync(string name, string ownerEmail, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MembershipEntity> InviteMemberAsync(Guid a, string e, AccountRole r, Guid by, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ChangeRoleAsync(Guid a, Guid t, AccountRole r, Guid actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveMemberAsync(Guid a, Guid t, Guid actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MemberView>> ListMembersAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AccountRole?> GetRoleAsync(Guid a, Guid u, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly JwtOptions Opts = new() { SigningKey = new string('k', 40), AccessTokenMinutes = 15, RefreshTokenDays = 30 };

    private static UserSessionService MakeSvc(ShortLynxDbContext ctx, AccountSummary? primary)
        => new(ctx, new StubAccountService(primary), Options.Create(Opts));

    private static async Task<UserAccountEntity> SeedUserAsync(TestDatabase db, bool isAdmin = false)
    {
        var user = EntityFactory.UserAccount($"{Guid.NewGuid():N}@example.com");
        user.IsAdmin = isAdmin;
        await using var ctx = db.CreateContext();
        ctx.UserAccountEntities.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Issue_ReturnsTokens_WithAccountAndRoleClaims()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = await SeedUserAsync(db, isAdmin: true);
        var accountId = Guid.CreateVersion7();

        var tokens = await MakeSvc(db.CreateContext(), new AccountSummary(accountId, "Acme", AccountRole.Admin))
            .IssueAsync(user, accountId, AccountRole.Admin);

        Assert.NotEmpty(tokens.AccessToken);
        Assert.NotEmpty(tokens.RefreshToken);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);
        Assert.Equal(user.Id.ToString(), jwt.GetClaim(JwtClaims.Subject).Value);
        Assert.Equal(accountId.ToString(), jwt.GetClaim(JwtClaims.AccountId).Value);
        Assert.Equal("Admin", jwt.GetClaim(JwtClaims.Role).Value);
        Assert.Equal("true", jwt.GetClaim(JwtClaims.IsAdmin).Value);

        await using var ctx = db.CreateContext();
        Assert.Equal(1, await ctx.RefreshTokenEntities.CountAsync(t => t.UserAccountId == user.Id));
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndInvalidatesOld()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = await SeedUserAsync(db);
        var accountId = Guid.CreateVersion7();
        var primary = new AccountSummary(accountId, "Acme", AccountRole.Owner);

        var first = await MakeSvc(db.CreateContext(), primary).IssueAsync(user, accountId, AccountRole.Owner);
        var second = await MakeSvc(db.CreateContext(), primary).RefreshAsync(first.RefreshToken);

        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // The old refresh token can no longer be used (it's revoked).
        var reuse = await MakeSvc(db.CreateContext(), primary).RefreshAsync(first.RefreshToken);
        Assert.Null(reuse);
    }

    [Fact]
    public async Task Refresh_ReuseOfRevokedToken_RevokesEntireChain()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = await SeedUserAsync(db);
        var primary = new AccountSummary(Guid.CreateVersion7(), "Acme", AccountRole.Member);

        var first = await MakeSvc(db.CreateContext(), primary).IssueAsync(user, primary.AccountId, primary.Role);
        var second = await MakeSvc(db.CreateContext(), primary).RefreshAsync(first.RefreshToken);
        Assert.NotNull(second);

        // Replaying the already-rotated token triggers reuse detection.
        var reuse = await MakeSvc(db.CreateContext(), primary).RefreshAsync(first.RefreshToken);
        Assert.Null(reuse);

        // …which also kills the legitimately-rotated token.
        var afterCascade = await MakeSvc(db.CreateContext(), primary).RefreshAsync(second.RefreshToken);
        Assert.Null(afterCascade);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = await SeedUserAsync(db);
        var primary = new AccountSummary(Guid.CreateVersion7(), "Acme", AccountRole.Member);

        var issued = await MakeSvc(db.CreateContext(), primary).IssueAsync(user, primary.AccountId, primary.Role);

        await using (var ctx = db.CreateContext())
            await ctx.RefreshTokenEntities
                .Where(t => t.UserAccountId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));

        Assert.Null(await MakeSvc(db.CreateContext(), primary).RefreshAsync(issued.RefreshToken));
    }

    [Fact]
    public async Task Revoke_PreventsRefresh()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = await SeedUserAsync(db);
        var primary = new AccountSummary(Guid.CreateVersion7(), "Acme", AccountRole.Member);

        var issued = await MakeSvc(db.CreateContext(), primary).IssueAsync(user, primary.AccountId, primary.Role);
        await MakeSvc(db.CreateContext(), primary).RevokeAsync(issued.RefreshToken);

        Assert.Null(await MakeSvc(db.CreateContext(), primary).RefreshAsync(issued.RefreshToken));
    }

    [Fact]
    public async Task Refresh_UnknownToken_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var primary = new AccountSummary(Guid.CreateVersion7(), "Acme", AccountRole.Member);
        Assert.Null(await MakeSvc(db.CreateContext(), primary).RefreshAsync("not-a-real-token"));
    }
}
