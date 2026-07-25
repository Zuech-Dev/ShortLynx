using ShortLynx.Services.Visits;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Visits;

public class LiveVisitQueriesTests
{
    [Fact]
    public async Task LoadSince_ReturnsClicksFromBothCodeSources_TaggedWithTheirLink()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        Guid accountId, linkId, userId;

        await using (var db = testDb.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            var sc = EntityFactory.ShortCode(link.Id, "live0001");
            var user = EntityFactory.UserAccount();
            var code = EntityFactory.UserLinkCode(link.Id, user.Id, "live0002");
            db.AddRange(account, link, sc, user, code);

            var shared = EntityFactory.Visit(sc.Id);
            shared.ClickedAt = since.AddMinutes(1);
            var attributed = EntityFactory.UserVisit(code.Id, user.Id);
            attributed.ClickedAt = since.AddMinutes(2);
            db.AddRange(shared, attributed);
            await db.SaveChangesAsync();
            (accountId, linkId, userId) = (account.Id, link.Id, user.Id);
        }

        await using (var db = testDb.CreateContext())
        {
            var rows = await LiveVisitQueries.LoadSinceAsync(db, accountId, since, 100);

            // A link's clicks live in two tables; a feed that reads only Visits silently loses Mode 2.
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(linkId, r.LinkId));
            Assert.Contains(rows, r => r.Code == "live0001" && r.UserId is null);
            Assert.Contains(rows, r => r.Code == "live0002" && r.UserId == userId);
        }
    }

    [Fact]
    public async Task LoadSince_ExcludesClicksAtOrBeforeTheCursor()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        Guid accountId;

        await using (var db = testDb.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            var sc = EntityFactory.ShortCode(link.Id, "live0003");
            db.AddRange(account, link, sc);

            var before = EntityFactory.Visit(sc.Id);
            before.ClickedAt = since.AddMinutes(-1);
            var exactlyAt = EntityFactory.Visit(sc.Id);
            exactlyAt.ClickedAt = since;
            var after = EntityFactory.Visit(sc.Id);
            after.ClickedAt = since.AddMinutes(1);
            db.AddRange(before, exactlyAt, after);
            await db.SaveChangesAsync();
            accountId = account.Id;
        }

        await using (var db = testDb.CreateContext())
        {
            var rows = await LiveVisitQueries.LoadSinceAsync(db, accountId, since, 100);

            // Strictly greater-than: the boundary row was already delivered on the poll that produced
            // this cursor, so including it would double-count every click at the window edge.
            var row = Assert.Single(rows);
            Assert.Equal(since.AddMinutes(1), row.ClickedAt);
        }
    }

    [Fact]
    public async Task LoadSince_NeverLeaksAnotherAccountsClicks()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        Guid mineId;

        await using (var db = testDb.CreateContext())
        {
            var mine = EntityFactory.Account("Mine");
            var theirs = EntityFactory.Account("Theirs");
            var myLink = EntityFactory.AnonymousLink(mine.Id);
            var theirLink = EntityFactory.AnonymousLink(theirs.Id);
            var myCode = EntityFactory.ShortCode(myLink.Id, "live0004");
            var theirCode = EntityFactory.ShortCode(theirLink.Id, "live0005");
            db.AddRange(mine, theirs, myLink, theirLink, myCode, theirCode);

            var myVisit = EntityFactory.Visit(myCode.Id);
            myVisit.ClickedAt = since.AddMinutes(1);
            var theirVisit = EntityFactory.Visit(theirCode.Id);
            theirVisit.ClickedAt = since.AddMinutes(1);
            db.AddRange(myVisit, theirVisit);
            await db.SaveChangesAsync();
            mineId = mine.Id;
        }

        await using (var db = testDb.CreateContext())
        {
            var rows = await LiveVisitQueries.LoadSinceAsync(db, mineId, since, 100);

            // The feed is pushed straight to a browser, so a scoping miss here is a cross-tenant leak.
            var row = Assert.Single(rows);
            Assert.Equal("live0004", row.Code);
        }
    }

    [Fact]
    public async Task LoadSince_OrdersOldestFirstAndHonoursTheLimit()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var since = DateTimeOffset.UtcNow.AddMinutes(-30);
        Guid accountId;

        await using (var db = testDb.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            var sc = EntityFactory.ShortCode(link.Id, "live0006");
            var user = EntityFactory.UserAccount();
            var code = EntityFactory.UserLinkCode(link.Id, user.Id, "live0007");
            db.AddRange(account, link, sc, user, code);

            // Interleave the two tables in time so ordering can't be satisfied by returning one
            // source's rows and then the other's.
            for (var i = 1; i <= 5; i++)
            {
                var v = EntityFactory.Visit(sc.Id);
                v.ClickedAt = since.AddMinutes(i * 2);
                var uv = EntityFactory.UserVisit(code.Id, user.Id);
                uv.ClickedAt = since.AddMinutes(i * 2 + 1);
                db.AddRange(v, uv);
            }
            await db.SaveChangesAsync();
            accountId = account.Id;
        }

        await using (var db = testDb.CreateContext())
        {
            var rows = await LiveVisitQueries.LoadSinceAsync(db, accountId, since, 4);

            // Oldest-first is what makes the caller's high-water mark advance correctly, and the cap
            // must bite after the merge — not per source, which would return 8.
            Assert.Equal(4, rows.Count);
            Assert.Equal(rows.Select(r => r.ClickedAt).OrderBy(t => t), rows.Select(r => r.ClickedAt));
            Assert.Equal(since.AddMinutes(2), rows[0].ClickedAt);
        }
    }

    [Fact]
    public async Task LoadSince_ReturnsEmptyForAnAccountWithNoLinks()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        Guid emptyId;

        await using (var db = testDb.CreateContext())
        {
            var account = EntityFactory.Account("Empty");
            db.Add(account);
            await db.SaveChangesAsync();
            emptyId = account.Id;
        }

        await using (var db = testDb.CreateContext())
        {
            // The no-codes path short-circuits before querying visits; it must not throw on the empty
            // IN (...) it would otherwise build.
            var rows = await LiveVisitQueries.LoadSinceAsync(db, emptyId, DateTimeOffset.UtcNow.AddDays(-1), 100);
            Assert.Empty(rows);
        }
    }
}
