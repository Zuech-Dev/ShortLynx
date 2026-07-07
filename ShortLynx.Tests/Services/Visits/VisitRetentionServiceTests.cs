using ShortLynx.Services.Visits;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Visits;

public class VisitRetentionServiceTests
{
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
