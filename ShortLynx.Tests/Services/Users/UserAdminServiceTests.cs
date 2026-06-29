using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.MagicLinks;
using ShortLynx.Services.Users;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Users;

public class UserAdminServiceTests
{
    private sealed class FakeMagic : IMagicLinkService
    {
        public readonly List<string> Sent = [];
        public Task<string> CreateTokenAsync(string email, CancellationToken ct = default)
        {
            Sent.Add(email);
            return Task.FromResult("token");
        }
        public Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult<UserAccountEntity?>(null);
    }

    private static (UserAdminService Svc, FakeMagic Magic) MakeSvc(ShortLynxDbContext ctx)
    {
        var magic = new FakeMagic();
        var accounts = new AccountService(ctx, magic);
        return (new UserAdminService(ctx, accounts, magic), magic);
    }

    [Fact]
    public async Task AddUser_NoAccount_CreatesOwnedAccount()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var (svc, magic) = MakeSvc(ctx);

        var view = await svc.AddUserAsync("New@Example.com", accountId: null, role: null, newAccountName: "Acme");

        Assert.Equal("new@example.com", view.Email);
        var account = Assert.Single(view.Accounts);
        Assert.Equal(AccountRole.Owner, account.Role);
        Assert.Equal("Acme", account.Name);
        Assert.Contains("new@example.com", magic.Sent);
    }

    [Fact]
    public async Task AddUser_ExistingAccount_AddsMembershipAtRole()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account("Globex");
        await using (var seed = db.CreateContext()) { seed.Add(account); await seed.SaveChangesAsync(); }

        await using var ctx = db.CreateContext();
        var (svc, _) = MakeSvc(ctx);
        var view = await svc.AddUserAsync("teammate@example.com", account.Id, AccountRole.Admin);

        var a = Assert.Single(view.Accounts);
        Assert.Equal(account.Id, a.AccountId);
        Assert.Equal(AccountRole.Admin, a.Role);
    }

    [Fact]
    public async Task AddUser_NonexistentAccount_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var (svc, _) = MakeSvc(ctx);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => svc.AddUserAsync("x@example.com", Guid.CreateVersion7(), AccountRole.Member));
    }

    [Fact]
    public async Task AssignToAccount_Upserts_AndChangesRole()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var user = EntityFactory.UserAccount("u@example.com");
        await using (var seed = db.CreateContext()) { seed.AddRange(account, user); await seed.SaveChangesAsync(); }

        await using (var ctx = db.CreateContext())
        {
            var (svc, _) = MakeSvc(ctx);
            Assert.True(await svc.AssignToAccountAsync(user.Id, account.Id, AccountRole.Member));
        }
        await using (var ctx = db.CreateContext())
        {
            var (svc, _) = MakeSvc(ctx);
            Assert.True(await svc.AssignToAccountAsync(user.Id, account.Id, AccountRole.Admin));
        }

        await using var verify = db.CreateContext();
        var memberships = await verify.MembershipEntities
            .Where(m => m.UserAccountId == user.Id && m.AccountId == account.Id).ToListAsync();
        var m = Assert.Single(memberships);
        Assert.Equal(AccountRole.Admin, m.Role);
    }

    [Fact]
    public async Task AssignToAccount_NonexistentUser_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        await using (var seed = db.CreateContext()) { seed.Add(account); await seed.SaveChangesAsync(); }

        await using var ctx = db.CreateContext();
        var (svc, _) = MakeSvc(ctx);
        Assert.False(await svc.AssignToAccountAsync(Guid.CreateVersion7(), account.Id, AccountRole.Member));
    }

    [Fact]
    public async Task RemoveFromAccount_RemovesMembership()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var user = EntityFactory.UserAccount("u@example.com");
        await using (var seed = db.CreateContext())
        {
            seed.AddRange(account, user);
            seed.MembershipEntities.Add(EntityFactory.Membership(account.Id, user.Id, AccountRole.Member));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            var (svc, _) = MakeSvc(ctx);
            Assert.True(await svc.RemoveFromAccountAsync(user.Id, account.Id));
        }

        await using var verify = db.CreateContext();
        Assert.Empty(verify.MembershipEntities.Where(m => m.UserAccountId == user.Id));
    }

    [Fact]
    public async Task SetSuperAdmin_And_SetActive_ToggleFlags()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount("u@example.com");
        user.IsActive = true;
        await using (var seed = db.CreateContext()) { seed.Add(user); await seed.SaveChangesAsync(); }

        await using (var ctx = db.CreateContext())
        {
            var (svc, _) = MakeSvc(ctx);
            Assert.True(await svc.SetSuperAdminAsync(user.Id, true));
            Assert.True(await svc.SetActiveAsync(user.Id, false));
        }

        await using var verify = db.CreateContext();
        var u = await verify.UserAccountEntities.SingleAsync(x => x.Id == user.Id);
        Assert.True(u.IsAdmin);
        Assert.False(u.IsActive);
    }
}
