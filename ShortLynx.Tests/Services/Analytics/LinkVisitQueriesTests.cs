using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Analytics;

// The single definition of "a link's clicks" — every analytics surface reads through here, so it earns
// direct coverage the ~7 hand-rolled sites it replaced never had.
public class LinkVisitQueriesTests
{
    private static VisitEntity Visit(Guid shortCodeId) => new()
    {
        Id = Guid.CreateVersion7(), ShortCodeId = shortCodeId,
        ClickedAt = DateTimeOffset.UtcNow, HashedIp = "h", Source = ClickSource.Bluesky, Device = DeviceType.Mobile,
    };

    private static UserVisitEntity UserVisit(Guid codeId) => new()
    {
        Id = Guid.CreateVersion7(), UserLinkCodeId = codeId,
        ClickedAt = DateTimeOffset.UtcNow, HashedIp = "h", Source = ClickSource.Direct, Device = DeviceType.Desktop,
    };

    [Fact]
    public async Task AnonymousLink_LoadsClicks_ThroughSharedCode()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var code = EntityFactory.ShortCode(link.Id, "abc12345");
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, link, code);
            ctx.AddRange(Visit(code.Id), Visit(code.Id), Visit(code.Id));
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        var rows = await LinkVisitQueries.LoadLinkRowsAsync(verify, link);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task UserAttributedLink_ClicksCount_AcrossRecipientCodes()
    {
        // The bug the consolidation fixed: Links.razor counted only shared-code Visits, so every
        // user-attributed link showed 0 clicks no matter how many recipients clicked.
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        link.Mode = LinkMode.UserAttributed;
        var c1 = new UserLinkCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, UserId = Guid.CreateVersion7(), Code = "u1", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var c2 = new UserLinkCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, UserId = Guid.CreateVersion7(), Code = "u2", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, link, c1, c2);
            ctx.AddRange(UserVisit(c1.Id), UserVisit(c1.Id), UserVisit(c2.Id)); // 3 total across 2 recipients
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        var counts = await LinkVisitQueries.CountByLinkAsync(verify, [link.Id]);
        Assert.Equal(3, counts[link.Id]); // previously 0

        var rows = await LinkVisitQueries.LoadLinkRowsAsync(verify, link);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task CodeCounts_IncludeZeroClickRecipients()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        link.Mode = LinkMode.UserAttributed;
        var clicked = new UserLinkCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, UserId = Guid.CreateVersion7(), Code = "hit", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var silent = new UserLinkCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, UserId = Guid.CreateVersion7(), Code = "miss", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, link, clicked, silent, UserVisit(clicked.Id));
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        var counts = await LinkVisitQueries.LoadCodeCountsAsync(verify, link);

        // A zero-click recipient is a real answer (the re-engage list) — it must appear, not be dropped.
        Assert.Equal(2, counts.Count);
        Assert.Equal(1, counts.Single(c => c.Code == "hit").Clicks);
        Assert.Equal(0, counts.Single(c => c.Code == "miss").Clicks);
    }

    [Fact]
    public async Task RowsByLink_TagsEachClickWithItsLink()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var linkA = EntityFactory.AnonymousLink(account.Id);
        var linkB = EntityFactory.AnonymousLink(account.Id);
        var codeA = EntityFactory.ShortCode(linkA.Id, "aaaa1111");
        var codeB = EntityFactory.ShortCode(linkB.Id, "bbbb2222");
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, linkA, linkB, codeA, codeB);
            ctx.AddRange(Visit(codeA.Id), Visit(codeA.Id), Visit(codeB.Id));
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        var tagged = await LinkVisitQueries.LoadRowsByLinkAsync(verify, [linkA.Id, linkB.Id]);

        Assert.Equal(2, tagged.Count(t => t.LinkId == linkA.Id));
        Assert.Equal(1, tagged.Count(t => t.LinkId == linkB.Id));
    }

    [Fact]
    public async Task CountForAccount_SumsBothCodeTypes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var anon = EntityFactory.AnonymousLink(account.Id);
        var attributed = EntityFactory.AnonymousLink(account.Id);
        attributed.Mode = LinkMode.UserAttributed;
        var sharedCode = EntityFactory.ShortCode(anon.Id, "shared00");
        var recipientCode = new UserLinkCodeEntity { Id = Guid.CreateVersion7(), LinkId = attributed.Id, UserId = Guid.CreateVersion7(), Code = "rcp", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, anon, attributed, sharedCode, recipientCode);
            ctx.AddRange(Visit(sharedCode.Id), Visit(sharedCode.Id), UserVisit(recipientCode.Id));
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        Assert.Equal(3, await LinkVisitQueries.CountForAccountAsync(verify, account.Id));
    }
}
