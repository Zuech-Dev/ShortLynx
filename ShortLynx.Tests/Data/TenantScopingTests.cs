using Microsoft.EntityFrameworkCore;

namespace ShortLynx.Tests.Data;

// Validates per-account (tenant) scoping. Resources are owned by an account (Link.AccountId), so scoping
// is simply `AccountId == currentAccountId`. An account must never see another's rows.
public class TenantScopingTests
{
    [Fact]
    public async Task LinksScopedByAccount_ReturnOnlyOwnRows()
    {
        await using var db = await TestDatabase.CreateAsync();

        var account1 = EntityFactory.Account("a1");
        var account2 = EntityFactory.Account("a2");
        var link1 = EntityFactory.AnonymousLink(account1.Id);
        var link2 = EntityFactory.AnonymousLink(account2.Id);

        await using (var seed = db.CreateContext())
        {
            seed.AccountEntities.AddRange(account1, account2);
            seed.LinkEntities.AddRange(link1, link2);
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext();

        var a1Links = await ctx.LinkEntities.Where(l => l.AccountId == account1.Id).ToListAsync();
        var a2Links = await ctx.LinkEntities.Where(l => l.AccountId == account2.Id).ToListAsync();

        Assert.Single(a1Links);
        Assert.Equal(link1.Id, a1Links[0].Id);
        Assert.Single(a2Links);
        Assert.Equal(link2.Id, a2Links[0].Id);
    }

    [Fact]
    public async Task LinkCount_ScopedByAccount_ExcludesOtherAccounts()
    {
        await using var db = await TestDatabase.CreateAsync();

        var account1 = EntityFactory.Account("only1");
        var account2 = EntityFactory.Account("only2");

        await using (var seed = db.CreateContext())
        {
            seed.AccountEntities.AddRange(account1, account2);
            seed.LinkEntities.AddRange(
                EntityFactory.AnonymousLink(account1.Id),
                EntityFactory.AnonymousLink(account1.Id),
                EntityFactory.AnonymousLink(account2.Id));
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext();
        var a1Count = await ctx.LinkEntities.CountAsync(l => l.AccountId == account1.Id);

        Assert.Equal(2, a1Count);
    }

    [Fact]
    public async Task KeyCreatedLinks_InheritKeyAccount_AndScopeWithDashboardLinks()
    {
        await using var db = await TestDatabase.CreateAsync();

        var account = EntityFactory.Account("shared");
        var key = EntityFactory.ApiKey(account.Id);

        // One link created via the key, one created in the dashboard — both own the same account.
        var keyLink = EntityFactory.AnonymousLink(account.Id);
        keyLink.ApiKeyId = key.Id;
        var dashLink = EntityFactory.AnonymousLink(account.Id);

        await using (var seed = db.CreateContext())
        {
            seed.AddRange(account, key, keyLink, dashLink);
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext();
        var links = await ctx.LinkEntities.Where(l => l.AccountId == account.Id).Select(l => l.Id).ToListAsync();

        Assert.Equal(2, links.Count);
        Assert.Contains(keyLink.Id, links);
        Assert.Contains(dashLink.Id, links);
    }
}
