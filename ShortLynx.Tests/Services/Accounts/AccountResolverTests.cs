using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Accounts;

public class AccountResolverTests
{
    // Seeds a user who owns two accounts; returns (userId, primaryAccountId [highest role], secondAccountId).
    private static async Task<(Guid UserId, Guid Primary, Guid Second)> SeedTwoAccountsAsync(TestDatabase db)
    {
        var user = EntityFactory.UserAccount();
        var primary = EntityFactory.Account("Primary");
        var second = EntityFactory.Account("Second");
        await using var ctx = db.CreateContext();
        ctx.AddRange(user, primary, second);
        // Owner of primary outranks Member of second, so primary is the fallback "highest-role" account.
        ctx.MembershipEntities.Add(EntityFactory.Membership(primary.Id, user.Id, AccountRole.Owner));
        ctx.MembershipEntities.Add(EntityFactory.Membership(second.Id, user.Id, AccountRole.Member));
        await ctx.SaveChangesAsync();
        return (user.Id, primary.Id, second.Id);
    }

    [Fact]
    public async Task Honors_Selected_Account_When_User_Is_A_Member()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (userId, _, second) = await SeedTwoAccountsAsync(db);

        await using var ctx = db.CreateContext();
        var resolved = await AccountResolver.ResolveAccountIdAsync(ctx, userId, second, "Personal");

        Assert.Equal(second, resolved);
    }

    [Fact]
    public async Task Ignores_Selected_Account_When_User_Is_Not_A_Member()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (userId, primary, _) = await SeedTwoAccountsAsync(db);
        var foreignAccount = Guid.CreateVersion7(); // user has no membership here

        await using var ctx = db.CreateContext();
        var resolved = await AccountResolver.ResolveAccountIdAsync(ctx, userId, foreignAccount, "Personal");

        // Falls back to the primary (highest-role) account — never leaks the foreign account.
        Assert.Equal(primary, resolved);
    }

    [Fact]
    public async Task Falls_Back_To_Primary_When_No_Selection()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (userId, primary, _) = await SeedTwoAccountsAsync(db);

        await using var ctx = db.CreateContext();
        var resolved = await AccountResolver.ResolveAccountIdAsync(ctx, userId, null, "Personal");

        Assert.Equal(primary, resolved);
    }
}
