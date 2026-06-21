using Microsoft.EntityFrameworkCore;
using ShortLynx.Repository;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Repository;

public class EfCoreDbOperationsTests
{
    // ── BulkInsertUserLinkCodesAsync ──────────────────────────────────────────

    [Fact]
    public async Task BulkInsertUserLinkCodesAsync_InsertsAll()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            ctx.AddRange(account, link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        var codes = Enumerable.Range(1, 5)
            .Select(i => EntityFactory.UserLinkCode(linkId, Guid.CreateVersion7(), $"code{i:D3}"))
            .ToList();

        await using (var ctx = db.CreateContext())
        {
            var ops = new EfCoreDbOperations(ctx);
            await ops.BulkInsertUserLinkCodesAsync(codes);
        }

        await using (var ctx = db.CreateContext())
        {
            var count = await ctx.UserLinkCodeEntities.CountAsync();
            Assert.Equal(5, count);
        }
    }

    [Fact]
    public async Task BulkInsertUserLinkCodesAsync_Empty_Succeeds()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var ops = new EfCoreDbOperations(ctx);
        await ops.BulkInsertUserLinkCodesAsync([]); // must not throw
    }

    [Fact]
    public async Task BulkInsertUserLinkCodesAsync_PreservesFieldValues()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            ctx.AddRange(account, link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        var userId = Guid.CreateVersion7();
        var original = EntityFactory.UserLinkCode(linkId, userId, "keepme1");
        original.IsOneTimeUse = true;

        await using (var ctx = db.CreateContext())
        {
            await new EfCoreDbOperations(ctx).BulkInsertUserLinkCodesAsync([original]);
        }

        await using (var ctx = db.CreateContext())
        {
            var saved = await ctx.UserLinkCodeEntities.SingleAsync(c => c.Code == "keepme1");
            Assert.Equal(linkId, saved.LinkId);
            Assert.Equal(userId, saved.UserId);
            Assert.True(saved.IsOneTimeUse);
            Assert.False(saved.IsUsed);
        }
    }

    // ── BulkInsertVisitsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task BulkInsertVisitsAsync_InsertsAll()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid shortCodeId;
        await using (var ctx = db.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            var sc = EntityFactory.ShortCode(link.Id, "vis000");
            ctx.AddRange(account, link, sc);
            await ctx.SaveChangesAsync();
            shortCodeId = sc.Id;
        }

        var visits = Enumerable.Range(1, 3)
            .Select(_ => EntityFactory.Visit(shortCodeId))
            .ToList();

        await using (var ctx = db.CreateContext())
        {
            await new EfCoreDbOperations(ctx).BulkInsertVisitsAsync(visits);
        }

        await using (var ctx = db.CreateContext())
        {
            var count = await ctx.VisitEntities.CountAsync();
            Assert.Equal(3, count);
        }
    }

    [Fact]
    public async Task BulkInsertVisitsAsync_Empty_Succeeds()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        await new EfCoreDbOperations(ctx).BulkInsertVisitsAsync([]);
    }

    // ── BulkInsertUserVisitsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task BulkInsertUserVisitsAsync_InsertsAll()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid userLinkCodeId;
        var userId = Guid.CreateVersion7();
        await using (var ctx = db.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            var ulc = EntityFactory.UserLinkCode(link.Id, userId, "uv0001");
            ctx.AddRange(account, link, ulc);
            await ctx.SaveChangesAsync();
            userLinkCodeId = ulc.Id;
        }

        var visits = Enumerable.Range(1, 4)
            .Select(_ => EntityFactory.UserVisit(userLinkCodeId, userId))
            .ToList();

        await using (var ctx = db.CreateContext())
        {
            await new EfCoreDbOperations(ctx).BulkInsertUserVisitsAsync(visits);
        }

        await using (var ctx = db.CreateContext())
        {
            var count = await ctx.UserVisitEntities.CountAsync();
            Assert.Equal(4, count);
        }
    }

    [Fact]
    public async Task BulkInsertUserVisitsAsync_Empty_Succeeds()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        await new EfCoreDbOperations(ctx).BulkInsertUserVisitsAsync([]);
    }
}
