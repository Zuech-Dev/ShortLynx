using Microsoft.EntityFrameworkCore;

namespace ShortLynx.Tests.Data;

// Validates the per-tenant scoping predicate used by the admin dashboard pages:
// a user's data is reached via Link.ApiKey.UserAccountId. A tenant must never see another's rows.
public class TenantScopingTests
{
    [Fact]
    public async Task LinksScopedByApiKeyOwner_ReturnOnlyOwnRows()
    {
        await using var db = await TestDatabase.CreateAsync();

        var user1 = EntityFactory.UserAccount("u1@example.com");
        var user2 = EntityFactory.UserAccount("u2@example.com");

        var key1 = EntityFactory.ApiKey("k1");
        key1.UserAccountId = user1.Id;
        var key2 = EntityFactory.ApiKey("k2");
        key2.Prefix = "TESTKEY2";
        key2.UserAccountId = user2.Id;

        var link1 = EntityFactory.AnonymousLink(key1.Id);
        var link2 = EntityFactory.AnonymousLink(key2.Id);

        await using (var seed = db.CreateContext())
        {
            seed.UserAccountEntities.AddRange(user1, user2);
            seed.ApiKeyEntities.AddRange(key1, key2);
            seed.LinkEntities.AddRange(link1, link2);
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext();

        var u1Links = await ctx.LinkEntities
            .Where(l => l.ApiKey.UserAccountId == user1.Id)
            .ToListAsync();
        var u2Links = await ctx.LinkEntities
            .Where(l => l.ApiKey.UserAccountId == user2.Id)
            .ToListAsync();

        Assert.Single(u1Links);
        Assert.Equal(link1.Id, u1Links[0].Id);
        Assert.Single(u2Links);
        Assert.Equal(link2.Id, u2Links[0].Id);
    }

    [Fact]
    public async Task LinkCount_ScopedByOwner_ExcludesOtherTenants()
    {
        await using var db = await TestDatabase.CreateAsync();

        var user1 = EntityFactory.UserAccount("only1@example.com");
        var user2 = EntityFactory.UserAccount("only2@example.com");
        var key1 = EntityFactory.ApiKey("k1");
        key1.UserAccountId = user1.Id;
        var key2 = EntityFactory.ApiKey("k2");
        key2.Prefix = "TESTKEY2";
        key2.UserAccountId = user2.Id;

        await using (var seed = db.CreateContext())
        {
            seed.UserAccountEntities.AddRange(user1, user2);
            seed.ApiKeyEntities.AddRange(key1, key2);
            seed.LinkEntities.AddRange(
                EntityFactory.AnonymousLink(key1.Id),
                EntityFactory.AnonymousLink(key1.Id),
                EntityFactory.AnonymousLink(key2.Id));
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext();
        var u1Count = await ctx.LinkEntities.CountAsync(l => l.ApiKey.UserAccountId == user1.Id);

        Assert.Equal(2, u1Count);
    }
}
