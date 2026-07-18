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
    public async Task PerPostCodeClicks_CountTowardTheLink_AndAttributeToTheirPost()
    {
        // The payoff of per-post attribution: two posts of the same link, on the same platform, get
        // distinct codes. Clicks on each land in Visits (same table as the shared code) and roll up to
        // the link — but each click's SocialPostCodeId ties it to exactly one post.
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var shared = EntityFactory.ShortCode(link.Id, "shared00");
        var postA = new SocialPostCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, Code = "postAAAA", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var postB = new SocialPostCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, Code = "postBBBB", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, link, shared, postA, postB);
            ctx.Add(Visit(shared.Id));                                  // 1 organic
            ctx.AddRange(PostVisit(postA.Id), PostVisit(postA.Id));    // 2 from post A
            ctx.Add(PostVisit(postB.Id));                              // 1 from post B
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        // All four count toward the link.
        Assert.Equal(4, (await LinkVisitQueries.CountByLinkAsync(verify, [link.Id]))[link.Id]);
        Assert.Equal(4, (await LinkVisitQueries.LoadLinkRowsAsync(verify, link)).Count);

        // And each post's clicks are separable — referrer alone never could.
        Assert.Equal(2, await verify.VisitEntities.CountAsync(v => v.SocialPostCodeId == postA.Id));
        Assert.Equal(1, await verify.VisitEntities.CountAsync(v => v.SocialPostCodeId == postB.Id));
    }

    [Fact]
    public async Task AttributionSplit_SeparatesPostsFromOrganic_AndRanksByClicks()
    {
        // The payoff: two posts on the SAME platform are separable (referrer sniffing never could),
        // and clicks on the link's shared code are reported as organic rather than mis-assigned.
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var shared = EntityFactory.ShortCode(link.Id, "shared00");

        var postA = new SocialPostEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, LinkId = link.Id,
            Platform = SocialPlatform.Bluesky, Handle = "me.bsky.social", ExternalPostId = "at://a",
            PostUrl = "https://bsky.app/a", Text = "a", PostedAt = DateTimeOffset.UtcNow.AddDays(-2),
            Impressions = null, Likes = 4,
        };
        var postB = new SocialPostEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, LinkId = link.Id,
            Platform = SocialPlatform.Bluesky, Handle = "me.bsky.social", ExternalPostId = "at://b",
            PostUrl = "https://bsky.app/b", Text = "b", PostedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Impressions = null, Likes = 40,
        };
        var codeA = new SocialPostCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, SocialPostId = postA.Id, Code = "codeAAAA", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var codeB = new SocialPostCodeEntity { Id = Guid.CreateVersion7(), LinkId = link.Id, SocialPostId = postB.Id, Code = "codeBBBB", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };

        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, link, shared, postA, postB, codeA, codeB);
            // Post A: 3 clicks from 2 distinct clickers. Post B: 1 click. Shared code: 2 organic.
            ctx.AddRange(PostVisit(codeA.Id, "ip1"), PostVisit(codeA.Id, "ip1"), PostVisit(codeA.Id, "ip2"));
            ctx.Add(PostVisit(codeB.Id, "ip3"));
            ctx.AddRange(Visit(shared.Id), Visit(shared.Id));
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        var split = await LinkVisitQueries.LoadAttributionSplitAsync(verify, link.Id);

        Assert.Equal(4, split.AttributedClicks);
        Assert.Equal(2, split.OrganicClicks);
        Assert.Equal(2, split.Posts.Count);

        // Ranked by clicks — the post with 40 likes drove FEWER clicks than the one with 4, which is
        // exactly the "vanity engagement vs. real traffic" insight this exists to surface.
        var top = split.Posts[0];
        Assert.Equal(postA.Id, top.SocialPostId);
        Assert.Equal(3, top.Clicks);
        Assert.Equal(2, top.UniqueClicks);
        Assert.Equal(4, top.Likes);
        Assert.Equal(1, split.Posts[1].Clicks);
        Assert.Equal(40, split.Posts[1].Likes);
    }

    [Fact]
    public async Task AttributionSplit_PostWithNoClicks_StillListed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var post = new SocialPostEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, LinkId = link.Id,
            Platform = SocialPlatform.Mastodon, Handle = "@me@m.social", ExternalPostId = "1",
            Text = "quiet", PostedAt = DateTimeOffset.UtcNow,
        };
        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(account, link, post);
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        var split = await LinkVisitQueries.LoadAttributionSplitAsync(verify, link.Id);

        // A post that drove zero clicks is a real answer — don't hide it.
        var only = Assert.Single(split.Posts);
        Assert.Equal(0, only.Clicks);
        Assert.Equal(0, split.AttributedClicks);
    }

    private static VisitEntity PostVisit(Guid postCodeId, string hashedIp = "h") => new()
    {
        Id = Guid.CreateVersion7(), SocialPostCodeId = postCodeId,
        ClickedAt = DateTimeOffset.UtcNow, HashedIp = hashedIp, Source = ClickSource.Direct, Device = DeviceType.Mobile,
    };

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
