using ShortLynx.Data.Entities;
using ShortLynx.Services.Visits;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Visits;

public class VisitRetentionServiceTests
{
    [Fact]
    public async Task PruneCityAggregatesOnce_UsesDifferentCutoffsPerTable()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var dailyCutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var visitorCutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        // Deliberately between the two cutoffs: old enough to be pruned from the short-lived visitor
        // table, but well within the 90-day daily-aggregate window -- proves the two run independently
        // rather than sharing one cutoff.
        var middleDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        var recentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var linkId = Guid.CreateVersion7();

        await using (var db = testDb.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            link.Id = linkId;
            db.AddRange(account, link);

            db.CityClickDailyEntities.AddRange(
                new CityClickDailyEntity { Id = Guid.CreateVersion7(), LinkId = linkId, City = "Chicago", Country = "US", Date = middleDate, Count = 6, UniqueCount = 6 },
                new CityClickDailyEntity { Id = Guid.CreateVersion7(), LinkId = linkId, City = "Chicago", Country = "US", Date = recentDate, Count = 6, UniqueCount = 6 });
            db.CityClickDailyVisitorEntities.AddRange(
                new CityClickDailyVisitorEntity { Id = Guid.CreateVersion7(), LinkId = linkId, City = "Chicago", Country = "US", Date = middleDate, HashedIp = "h1" },
                new CityClickDailyVisitorEntity { Id = Guid.CreateVersion7(), LinkId = linkId, City = "Chicago", Country = "US", Date = recentDate, HashedIp = "h2" });
            await db.SaveChangesAsync();
        }

        await using (var db = testDb.CreateContext())
        {
            var removed = await VisitRetentionService.PruneCityAggregatesOnceAsync(db, dailyCutoff, visitorCutoff);
            Assert.Equal(1, removed); // only the middle-date visitor row: older than 2 days, but newer than 90
        }

        await using (var check = testDb.CreateContext())
        {
            Assert.Equal(2, check.CityClickDailyEntities.Count());     // both survive -- neither is >90 days old
            Assert.Single(check.CityClickDailyVisitorEntities);        // only the recent one survives
            Assert.Equal(recentDate, check.CityClickDailyVisitorEntities.Single().Date);
        }
    }

    [Fact]
    public async Task PruneOnce_DeletesOnlyRowsOlderThanCutoff_AcrossBothModes()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        Guid scId, codeId;
        await using (var db = testDb.CreateContext())
        {
            var account = EntityFactory.Account();
            var link = EntityFactory.AnonymousLink(account.Id);
            var sc = EntityFactory.ShortCode(link.Id, "ret12345");
            var user = EntityFactory.UserAccount();
            var code = EntityFactory.UserLinkCode(link.Id, user.Id, "usr12345");
            db.AddRange(account, link, sc, user, code);

            var oldVisit = EntityFactory.Visit(sc.Id);
            oldVisit.ClickedAt = cutoff.AddDays(-1);
            var newVisit = EntityFactory.Visit(sc.Id);
            newVisit.ClickedAt = cutoff.AddDays(1);
            var oldUserVisit = EntityFactory.UserVisit(code.Id, user.Id);
            oldUserVisit.ClickedAt = cutoff.AddDays(-1);
            var newUserVisit = EntityFactory.UserVisit(code.Id, user.Id);
            newUserVisit.ClickedAt = cutoff.AddDays(1);
            db.AddRange(oldVisit, newVisit, oldUserVisit, newUserVisit);
            await db.SaveChangesAsync();
            (scId, codeId) = (sc.Id, code.Id);
        }

        await using (var db = testDb.CreateContext())
        {
            var removed = await VisitRetentionService.PruneOnceAsync(db, cutoff);
            Assert.Equal(2, removed);
        }

        await using (var check = testDb.CreateContext())
        {
            var visit = Assert.Single(check.VisitEntities.Where(v => v.ShortCodeId == scId));
            Assert.True(visit.ClickedAt >= cutoff);
            var userVisit = Assert.Single(check.UserVisitEntities.Where(v => v.UserLinkCodeId == codeId));
            Assert.True(userVisit.ClickedAt >= cutoff);
        }
    }
}
