using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Analytics;

/// <summary>
/// Plan retention is enforced by HIDING clicks before a cutoff, never deleting them. Every query that
/// takes <c>since</c> must agree on the same window, and the rows themselves must survive so an upgrade
/// brings the history back.
/// </summary>
public class RetentionCutoffQueriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Cutoff = Now.AddDays(-30);

    private static VisitEntity Visit(Guid shortCodeId, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(), ShortCodeId = shortCodeId, ClickedAt = at,
        HashedIp = Guid.NewGuid().ToString("N"), Source = ClickSource.Direct, Device = DeviceType.Desktop,
    };

    private static UserVisitEntity UserVisit(Guid codeId, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(), UserLinkCodeId = codeId, ClickedAt = at,
        HashedIp = "h", Source = ClickSource.Direct, Device = DeviceType.Desktop,
    };

    // 2 recent + 3 old clicks on an anonymous link.
    private static async Task<(TestDatabase Db, LinkEntity Link, Guid AccountId)> SeedAnonymousAsync()
    {
        var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var code = EntityFactory.ShortCode(link.Id, "ret12345");
        await using var ctx = db.CreateContext();
        ctx.AddRange(account, link, code);
        ctx.AddRange(Visit(code.Id, Now.AddDays(-1)), Visit(code.Id, Now.AddDays(-29)),
                     Visit(code.Id, Now.AddDays(-31)), Visit(code.Id, Now.AddDays(-90)), Visit(code.Id, Now.AddDays(-400)));
        ctx.Add(new CityClickDailyEntity
        {
            Id = Guid.CreateVersion7(), LinkId = link.Id, City = "Old", Country = "US",
            Date = DateOnly.FromDateTime(Now.AddDays(-60).UtcDateTime), Count = 9, UniqueCount = 9,
        });
        ctx.Add(new CityClickDailyEntity
        {
            Id = Guid.CreateVersion7(), LinkId = link.Id, City = "New", Country = "US",
            Date = DateOnly.FromDateTime(Now.AddDays(-2).UtcDateTime), Count = 4, UniqueCount = 4,
        });
        await ctx.SaveChangesAsync();
        return (db, link, account.Id);
    }

    [Fact]
    public async Task AllQueries_HideClicksBeforeCutoff_AndAgreeOnTheCount()
    {
        var (db, link, accountId) = await SeedAnonymousAsync();
        await using var _ = db;
        await using var ctx = db.CreateContext();

        Assert.Equal(2, (await LinkVisitQueries.LoadLinkRowsAsync(ctx, link, Cutoff)).Count);
        Assert.Equal(2, (await LinkVisitQueries.CountByLinkAsync(ctx, [link.Id], Cutoff))[link.Id]);
        Assert.Equal(2, Assert.Single(await LinkVisitQueries.LoadCodeCountsAsync(ctx, link, Cutoff)).Clicks);
        Assert.Equal(2, (await LinkVisitQueries.LoadAttributionSplitAsync(ctx, link.Id, Cutoff)).OrganicClicks);
        Assert.Equal(2, await LinkVisitQueries.CountForAccountAsync(ctx, accountId, Cutoff));

        var city = Assert.Single(await CityClickQueries.LoadForLinksAsync(ctx, [link.Id], Cutoff));
        Assert.Equal("New", city.City);
    }

    [Fact]
    public async Task NoCutoff_ReturnsFullHistory_SoHiddenClicksComeBackOnUpgrade()
    {
        var (db, link, accountId) = await SeedAnonymousAsync();
        await using var _ = db;
        await using var ctx = db.CreateContext();

        Assert.Equal(5, (await LinkVisitQueries.LoadLinkRowsAsync(ctx, link)).Count);
        Assert.Equal(5, (await LinkVisitQueries.CountByLinkAsync(ctx, [link.Id]))[link.Id]);
        Assert.Equal(5, Assert.Single(await LinkVisitQueries.LoadCodeCountsAsync(ctx, link)).Clicks);
        Assert.Equal(5, await LinkVisitQueries.CountForAccountAsync(ctx, accountId));
        Assert.Equal(2, (await CityClickQueries.LoadForLinksAsync(ctx, [link.Id])).Count);
        Assert.Equal(5, ctx.VisitEntities.Count()); // nothing was deleted by the filtered reads
    }

    [Fact]
    public async Task RecipientCodeCounts_RespectCutoff_AndKeepZeroClickRecipients()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        link.Mode = LinkMode.UserAttributed;
        var hit = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "hit");
        var oldOnly = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "old");
        await using (var seed = db.CreateContext())
        {
            seed.AddRange(account, link, hit, oldOnly);
            seed.AddRange(UserVisit(hit.Id, Now.AddDays(-3)), UserVisit(hit.Id, Now.AddDays(-45)), UserVisit(oldOnly.Id, Now.AddDays(-45)));
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateContext();
        var counts = (await LinkVisitQueries.LoadCodeCountsAsync(ctx, link, Cutoff)).ToDictionary(c => c.Code, c => c.Clicks);

        Assert.Equal(1, counts["hit"]);
        Assert.Equal(0, counts["old"]);
        Assert.Equal(1, (await LinkVisitQueries.CountByLinkAsync(ctx, [link.Id], Cutoff))[link.Id]);
    }

    [Fact]
    public void Clamp_TakesTheLaterOfRequestAndCutoff()
    {
        Assert.Equal(Cutoff, RetentionCutoff.Clamp(Now.AddDays(-365), Cutoff));
        Assert.Equal(Now.AddDays(-1), RetentionCutoff.Clamp(Now.AddDays(-1), Cutoff));
        Assert.Equal(Now.AddDays(-365), RetentionCutoff.Clamp(Now.AddDays(-365), null));
    }
}
