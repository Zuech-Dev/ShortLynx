using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Data;

public class AccountConstraintTests
{
    [Fact]
    public async Task Membership_IsUnique_PerAccountAndUser()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var user = EntityFactory.UserAccount();

        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, user);
            ctx.MembershipEntities.Add(EntityFactory.Membership(account.Id, user.Id, AccountRole.Owner));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            // Same (account, user) again — different role — must violate the unique index.
            ctx.MembershipEntities.Add(EntityFactory.Membership(account.Id, user.Id, AccountRole.Member));
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task SameUser_CanBelongToMultipleAccounts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a1 = EntityFactory.Account("A1");
        var a2 = EntityFactory.Account("A2");
        var user = EntityFactory.UserAccount();

        await using var ctx = db.CreateContext();
        ctx.AddRange(a1, a2, user);
        ctx.MembershipEntities.Add(EntityFactory.Membership(a1.Id, user.Id, AccountRole.Owner));
        ctx.MembershipEntities.Add(EntityFactory.Membership(a2.Id, user.Id, AccountRole.Member));
        await ctx.SaveChangesAsync();

        Assert.Equal(2, await ctx.MembershipEntities.CountAsync(m => m.UserAccountId == user.Id));
    }

    [Fact]
    public async Task DeletingAccount_CascadesMemberships()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var user = EntityFactory.UserAccount();

        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, user);
            ctx.MembershipEntities.Add(EntityFactory.Membership(account.Id, user.Id));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            ctx.AccountEntities.Remove(await ctx.AccountEntities.FindAsync(account.Id) ?? throw new());
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
            Assert.False(await ctx.MembershipEntities.AnyAsync(m => m.AccountId == account.Id));
    }

    [Fact]
    public async Task DeletingUser_CascadesMemberships()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var user = EntityFactory.UserAccount();

        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, user);
            ctx.MembershipEntities.Add(EntityFactory.Membership(account.Id, user.Id));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            ctx.UserAccountEntities.Remove(await ctx.UserAccountEntities.FindAsync(user.Id) ?? throw new());
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
            Assert.False(await ctx.MembershipEntities.AnyAsync(m => m.UserAccountId == user.Id));
    }
}
