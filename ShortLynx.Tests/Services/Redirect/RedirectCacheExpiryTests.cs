using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Redirect;

namespace ShortLynx.Tests.Services.Redirect;

/// <summary>
/// The redirect cache's staleness bound. Web serves redirects in a different process from Core, so
/// nothing evicts a cached entry when a link is edited — the absolute cap is the only thing that makes
/// an edit reach a link that keeps getting clicked. Each test clicks every 30s (inside the 300s sliding
/// window, so sliding expiry alone would never reload it), changes the row directly in the database,
/// and checks the change is invisible before the cap and visible after it.
/// </summary>
public class RedirectCacheExpiryTests
{
    private const int CapSeconds = 60;

    private sealed class FakeClock : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class Harness(TestDatabase db, int absoluteSeconds)
    {
        public FakeClock Clock { get; } = new();
        private IMemoryCache? _cache;

        private IMemoryCache Cache => _cache ??= new MemoryCache(new MemoryCacheOptions { Clock = Clock });

        // A fresh service and context per click, as in Web (scoped per request); only the cache is shared.
        public Task<RedirectCacheEntry?> ClickAsync(string code, string? host = null)
            => Service().LookupAsync(code, host);

        public Task<RedirectCacheEntry?> ClickCustomAsync(string code, string? host = null)
            => Service().LookupCustomAsync(code, host);

        private RedirectService Service() => new(
            db.CreateContext(),
            Cache,
            Options.Create(new RedirectOptions
            {
                CacheSlidingExpirationSeconds = 300,
                CacheAbsoluteExpirationSeconds = absoluteSeconds,
            }));

        public async Task MutateAsync(Func<ShortLynxDbContext, Task> change)
        {
            await using var ctx = db.CreateContext();
            await change(ctx);
            await ctx.SaveChangesAsync();
        }

        public void Advance(int seconds) => Clock.UtcNow += TimeSpan.FromSeconds(seconds);
    }

    private static async Task<LinkEntity> SeedLinkAsync(
        TestDatabase db, string code, bool isCustom = false, Action<AccountEntity>? account = null)
    {
        var acc = EntityFactory.Account();
        account?.Invoke(acc);
        var link = EntityFactory.AnonymousLink(acc.Id);
        await using var ctx = db.CreateContext();
        ctx.AddRange(acc, link);
        ctx.ShortCodeEntities.Add(EntityFactory.ShortCode(link.Id, code, isCustom));
        await ctx.SaveChangesAsync();
        return link;
    }

    // ── Each cached field reaches a hot link within the cap ─────────────────

    [Fact]
    public async Task HotLink_DestinationChange_ArrivesAfterCap()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "dest0001");
        var h = new Harness(db, CapSeconds);

        Assert.Equal("https://example.com", (await h.ClickAsync("dest0001"))!.OriginalUrl);
        await h.MutateAsync(async ctx =>
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).OriginalUrl = "https://example.org/new");

        h.Advance(30);
        Assert.Equal("https://example.com", (await h.ClickAsync("dest0001"))!.OriginalUrl);

        h.Advance(31);
        Assert.Equal("https://example.org/new", (await h.ClickAsync("dest0001"))!.OriginalUrl);
    }

    [Fact]
    public async Task HotLink_CampaignUtmChange_ArrivesAfterCap()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "utm00001");
        var campaignId = Guid.CreateVersion7();
        await using (var ctx = db.CreateContext())
        {
            ctx.Add(new CampaignEntity
            {
                Id = campaignId,
                AccountId = link.AccountId,
                Name = "Launch",
                UtmSource = "newsletter",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).CampaignId = campaignId;
            await ctx.SaveChangesAsync();
        }
        var h = new Harness(db, CapSeconds);

        Assert.Contains("utm_source=newsletter", (await h.ClickAsync("utm00001"))!.OriginalUrl);
        await h.MutateAsync(async ctx =>
            (await ctx.CampaignEntities.SingleAsync(c => c.Id == campaignId)).UtmSource = "sms");

        h.Advance(30);
        Assert.Contains("utm_source=newsletter", (await h.ClickAsync("utm00001"))!.OriginalUrl);

        h.Advance(31);
        Assert.Contains("utm_source=sms", (await h.ClickAsync("utm00001"))!.OriginalUrl);
    }

    [Fact]
    public async Task HotLink_DomainPin_ArrivesAfterCap()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "pin00001");
        var domain = EntityFactory.CustomDomain(link.AccountId, "go.example.com");
        await using (var ctx = db.CreateContext())
        {
            ctx.Add(domain);
            await ctx.SaveChangesAsync();
        }
        var h = new Harness(db, CapSeconds);

        Assert.NotNull(await h.ClickAsync("pin00001", "sho.rt"));
        await h.MutateAsync(async ctx =>
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).CustomDomainId = domain.Id);

        h.Advance(30);
        Assert.NotNull(await h.ClickAsync("pin00001", "sho.rt"));

        h.Advance(31);
        Assert.Null(await h.ClickAsync("pin00001", "sho.rt"));
        Assert.NotNull(await h.ClickAsync("pin00001", "go.example.com"));
    }

    [Fact]
    public async Task HotLink_Deactivation_ArrivesAfterCap()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "off00001");
        var h = new Harness(db, CapSeconds);

        Assert.NotNull(await h.ClickAsync("off00001"));
        await h.MutateAsync(async ctx =>
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).IsActive = false);

        h.Advance(30);
        Assert.NotNull(await h.ClickAsync("off00001"));

        h.Advance(31);
        Assert.Null(await h.ClickAsync("off00001"));
    }

    [Fact]
    public async Task HotMode2Link_PrivacyPolicyRemoved_DisclosureRequiredAfterCap()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "unused01",
            account: a => a.PrivacyPolicyUrl = "https://example.com/privacy");
        await using (var ctx = db.CreateContext())
        {
            ctx.UserLinkCodeEntities.Add(EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "disc0001"));
            await ctx.SaveChangesAsync();
        }
        var h = new Harness(db, CapSeconds);

        Assert.False((await h.ClickAsync("disc0001"))!.DisclosureRequired);
        await h.MutateAsync(async ctx =>
            (await ctx.AccountEntities.SingleAsync(a => a.Id == link.AccountId)).PrivacyPolicyUrl = null);

        h.Advance(30);
        Assert.False((await h.ClickAsync("disc0001"))!.DisclosureRequired);

        h.Advance(31);
        Assert.True((await h.ClickAsync("disc0001"))!.DisclosureRequired);
    }

    [Fact]
    public async Task HotCustomCode_DestinationAndDeactivation_ArriveAfterCap()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "spring-sale", isCustom: true);
        var h = new Harness(db, CapSeconds);

        Assert.Equal("https://example.com", (await h.ClickCustomAsync("spring-sale"))!.OriginalUrl);
        await h.MutateAsync(async ctx =>
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).OriginalUrl = "https://example.org/new");

        h.Advance(30);
        Assert.Equal("https://example.com", (await h.ClickCustomAsync("spring-sale"))!.OriginalUrl);

        h.Advance(31);
        Assert.Equal("https://example.org/new", (await h.ClickCustomAsync("spring-sale"))!.OriginalUrl);

        await h.MutateAsync(async ctx =>
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).IsActive = false);
        h.Advance(CapSeconds + 1);
        Assert.Null(await h.ClickCustomAsync("spring-sale"));
    }

    // ── The cap is what makes the difference ────────────────────────────────

    [Fact]
    public async Task CapDisabled_HotLinkStaysStale_SlidingExpiryAlone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var link = await SeedLinkAsync(db, "stale001");
        var h = new Harness(db, absoluteSeconds: 0);

        await h.ClickAsync("stale001");
        await h.MutateAsync(async ctx =>
            (await ctx.LinkEntities.SingleAsync(l => l.Id == link.Id)).IsActive = false);

        // Ten minutes of clicks every 30s: the sliding window never lapses, so the deactivated link keeps
        // redirecting. This is the behavior before the cap existed.
        for (var i = 0; i < 20; i++)
        {
            h.Advance(30);
            Assert.NotNull(await h.ClickAsync("stale001"));
        }
    }

    [Fact]
    public void DefaultOptions_CapIsSixtySeconds()
        => Assert.Equal(60, new RedirectOptions().CacheAbsoluteExpirationSeconds);
}
