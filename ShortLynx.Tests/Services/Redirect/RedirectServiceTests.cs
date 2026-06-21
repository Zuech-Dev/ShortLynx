using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ShortLynx.Services.Redirect;

namespace ShortLynx.Tests.Services.Redirect;

public class RedirectServiceTests
{
    private static RedirectService MakeSvc(ShortLynx.Data.Context.ShortLynxDbContext ctx, IMemoryCache? cache = null)
    {
        var opts = Options.Create(new RedirectOptions { CacheSlidingExpirationSeconds = 300 });
        return new RedirectService(ctx, cache ?? new MemoryCache(new MemoryCacheOptions()), opts);
    }

    private static async Task<(ShortLynx.Data.Entities.ApiKeyEntity Key, ShortLynx.Data.Entities.LinkEntity Link)>
        SeedLinkAsync(TestDatabase db)
    {
        var key = EntityFactory.ApiKey();
        var link = EntityFactory.AnonymousLink(key.Id);
        await using var ctx = db.CreateContext();
        ctx.AddRange(key, link);
        await ctx.SaveChangesAsync();
        return (key, link);
    }

    // Seeds a user + verified custom domain + a link pinned to that domain. Returns the link and host.
    private static async Task<(ShortLynx.Data.Entities.LinkEntity Link, string Host)>
        SeedPinnedLinkAsync(TestDatabase db, string host = "go.example.com")
    {
        var user = EntityFactory.UserAccount($"{Guid.NewGuid():N}@example.com");
        var domain = EntityFactory.CustomDomain(user.Id, host);
        var link = EntityFactory.UserOwnedLink(user.Id);
        link.CustomDomainId = domain.Id;

        await using var ctx = db.CreateContext();
        ctx.AddRange(user, domain, link);
        await ctx.SaveChangesAsync();
        return (link, host);
    }

    // ── Unknown code ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_UnknownCode_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var result = await MakeSvc(db.CreateContext()).LookupAsync("notexist");
        Assert.Null(result);
    }

    // ── Mode 1 — anonymous short code ────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_Mode1_ReturnsEntry()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            ctx.ShortCodeEntities.Add(EntityFactory.ShortCode(link.Id, "abc12345"));
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).LookupAsync("abc12345");

        Assert.NotNull(result);
        Assert.Equal(link.OriginalUrl, result.OriginalUrl);
        Assert.NotNull(result.ShortCodeId);
        Assert.Null(result.UserLinkCodeId);
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task LookupAsync_Mode1_InactiveCode_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            var sc = EntityFactory.ShortCode(link.Id, "inactive1");
            sc.IsActive = false;
            ctx.ShortCodeEntities.Add(sc);
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).LookupAsync("inactive1");
        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_Mode1_InactiveLink_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = EntityFactory.ApiKey();
        var link = EntityFactory.AnonymousLink(key.Id);
        link.IsActive = false;

        await using (var ctx = db.CreateContext())
        {
            ctx.AddRange(key, link);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            ctx.ShortCodeEntities.Add(EntityFactory.ShortCode(link.Id, "deadlink"));
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).LookupAsync("deadlink");
        Assert.Null(result);
    }

    // ── Mode 2 — user-attributed code ────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_Mode2_ReturnsEntry()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);
        var userId = Guid.CreateVersion7();

        Guid ulcId;
        await using (var ctx = db.CreateContext())
        {
            var ulc = EntityFactory.UserLinkCode(link.Id, userId, "usr12345");
            ctx.UserLinkCodeEntities.Add(ulc);
            await ctx.SaveChangesAsync();
            ulcId = ulc.Id;
        }

        var result = await MakeSvc(db.CreateContext()).LookupAsync("usr12345");

        Assert.NotNull(result);
        Assert.Equal(link.OriginalUrl, result.OriginalUrl);
        Assert.Null(result.ShortCodeId);
        Assert.Equal(ulcId, result.UserLinkCodeId);
        Assert.Equal(userId, result.UserId);
    }

    [Fact]
    public async Task LookupAsync_Mode2_UsedOneTimeCode_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            var ulc = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "onetimer");
            ulc.IsOneTimeUse = true;
            ulc.IsUsed = true;
            ctx.UserLinkCodeEntities.Add(ulc);
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).LookupAsync("onetimer");
        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_Mode2_UnusedOneTimeCode_ReturnsEntry()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            var ulc = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "onefresh");
            ulc.IsOneTimeUse = true;
            ulc.IsUsed = false;
            ctx.UserLinkCodeEntities.Add(ulc);
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).LookupAsync("onefresh");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task LookupAsync_Mode2_OneTimeCode_ResolvesOnceThenNull_AndMarksUsed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        Guid ulcId;
        await using (var ctx = db.CreateContext())
        {
            var ulc = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "claimme1");
            ulc.IsOneTimeUse = true;
            ctx.UserLinkCodeEntities.Add(ulc);
            await ctx.SaveChangesAsync();
            ulcId = ulc.Id;
        }

        // Shared cache so we exercise the same path a real request would (one-time codes aren't cached).
        var sharedCache = new MemoryCache(new MemoryCacheOptions());

        var first = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("claimme1");
        Assert.NotNull(first);

        // The first lookup itself must have claimed the code.
        await using (var ctx = db.CreateContext())
        {
            var ulc = await ctx.UserLinkCodeEntities.FindAsync(ulcId);
            Assert.True(ulc!.IsUsed);
        }

        var second = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("claimme1");
        Assert.Null(second);
    }

    [Fact]
    public async Task LookupAsync_Mode2_MultiUseCode_ResolvesRepeatedly()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            // EntityFactory.UserLinkCode sets IsOneTimeUse = false.
            ctx.UserLinkCodeEntities.Add(EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "multiuse"));
            await ctx.SaveChangesAsync();
        }

        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var first = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("multiuse");
        var second = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("multiuse");

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    // ── Cache behaviour ───────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_SecondCall_HitsCache_EvenAfterDbRowDeleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        Guid scId;
        await using (var ctx = db.CreateContext())
        {
            var sc = EntityFactory.ShortCode(link.Id, "cached00");
            ctx.ShortCodeEntities.Add(sc);
            await ctx.SaveChangesAsync();
            scId = sc.Id;
        }

        // Shared cache instance — both service instances see the same cache.
        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var firstResult = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("cached00");
        Assert.NotNull(firstResult);

        // Delete the row from DB.
        await using (var ctx = db.CreateContext())
        {
            var sc = await ctx.ShortCodeEntities.FindAsync(scId);
            ctx.ShortCodeEntities.Remove(sc!);
            await ctx.SaveChangesAsync();
        }

        // Second lookup still returns the cached entry.
        var secondResult = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("cached00");
        Assert.NotNull(secondResult);
        Assert.Equal(firstResult.OriginalUrl, secondResult.OriginalUrl);
    }

    [Fact]
    public async Task LookupAsync_Mode2_OneTimeCode_IsNotCached()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        Guid ulcId;
        await using (var ctx = db.CreateContext())
        {
            var ulc = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "notcache");
            ulc.IsOneTimeUse = true;
            ctx.UserLinkCodeEntities.Add(ulc);
            await ctx.SaveChangesAsync();
            ulcId = ulc.Id;
        }

        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var first = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("notcache");
        Assert.NotNull(first);

        // Mark as used directly in DB.
        await using (var ctx = db.CreateContext())
        {
            var ulc = await ctx.UserLinkCodeEntities.FindAsync(ulcId);
            ulc!.IsUsed = true;
            await ctx.SaveChangesAsync();
        }

        // Because one-time-use codes aren't cached, the second lookup should return null.
        var second = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("notcache");
        Assert.Null(second);
    }

    [Fact]
    public async Task LookupAsync_UnknownCode_NegativeResultIsCached()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        var sharedCache = new MemoryCache(new MemoryCacheOptions());

        // First lookup misses and caches the negative sentinel.
        var first = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("ghostcode");
        Assert.Null(first);

        // Now create a matching code in the DB.
        await using (var ctx = db.CreateContext())
        {
            ctx.ShortCodeEntities.Add(EntityFactory.ShortCode(link.Id, "ghostcode"));
            await ctx.SaveChangesAsync();
        }

        // The second lookup still returns null because the miss was cached (didn't re-hit the DB).
        var second = await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("ghostcode");
        Assert.Null(second);
    }

    // ── Custom-domain pinning ─────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_PinnedLink_ResolvesOnlyOnMatchingHost()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (link, host) = await SeedPinnedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            ctx.ShortCodeEntities.Add(EntityFactory.ShortCode(link.Id, "pinned01"));
            await ctx.SaveChangesAsync();
        }

        var match = await MakeSvc(db.CreateContext()).LookupAsync("pinned01", host);
        Assert.NotNull(match);

        var wrongHost = await MakeSvc(db.CreateContext()).LookupAsync("pinned01", "evil.example.com");
        Assert.Null(wrongHost);

        var noHost = await MakeSvc(db.CreateContext()).LookupAsync("pinned01");
        Assert.Null(noHost);
    }

    [Fact]
    public async Task LookupAsync_PinnedLink_WrongHostFromCache_StillRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (link, host) = await SeedPinnedLinkAsync(db);

        await using (var ctx = db.CreateContext())
        {
            ctx.ShortCodeEntities.Add(EntityFactory.ShortCode(link.Id, "pinned02"));
            await ctx.SaveChangesAsync();
        }

        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        // Prime the cache with a matching-host lookup, then a wrong-host lookup must still be rejected.
        Assert.NotNull(await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("pinned02", host));
        Assert.Null(await MakeSvc(db.CreateContext(), sharedCache).LookupAsync("pinned02", "evil.example.com"));
    }

    [Fact]
    public async Task LookupAsync_PinnedOneTimeCode_WrongHost_DoesNotConsume()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (link, host) = await SeedPinnedLinkAsync(db);

        Guid ulcId;
        await using (var ctx = db.CreateContext())
        {
            var ulc = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "pin1time");
            ulc.IsOneTimeUse = true;
            ctx.UserLinkCodeEntities.Add(ulc);
            await ctx.SaveChangesAsync();
            ulcId = ulc.Id;
        }

        // Wrong host is rejected and must NOT burn the one-time code.
        Assert.Null(await MakeSvc(db.CreateContext()).LookupAsync("pin1time", "evil.example.com"));
        await using (var ctx = db.CreateContext())
            Assert.False((await ctx.UserLinkCodeEntities.FindAsync(ulcId))!.IsUsed);

        // Correct host resolves and consumes it.
        Assert.NotNull(await MakeSvc(db.CreateContext()).LookupAsync("pin1time", host));
        await using (var ctx = db.CreateContext())
            Assert.True((await ctx.UserLinkCodeEntities.FindAsync(ulcId))!.IsUsed);
    }

    [Fact]
    public async Task LookupAsync_WithSizeLimitedCache_CachesFoundEntry()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, link) = await SeedLinkAsync(db);

        Guid scId;
        await using (var ctx = db.CreateContext())
        {
            var sc = EntityFactory.ShortCode(link.Id, "sizecap0");
            ctx.ShortCodeEntities.Add(sc);
            await ctx.SaveChangesAsync();
            scId = sc.Id;
        }

        // A size-limited cache requires every entry to declare a Size — the service sets Size 1.
        var sizedCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

        var first = await MakeSvc(db.CreateContext(), sizedCache).LookupAsync("sizecap0");
        Assert.NotNull(first);

        // Delete the row; the entry must still be served from cache (proving it was stored with a Size).
        await using (var ctx = db.CreateContext())
        {
            var sc = await ctx.ShortCodeEntities.FindAsync(scId);
            ctx.ShortCodeEntities.Remove(sc!);
            await ctx.SaveChangesAsync();
        }

        var second = await MakeSvc(db.CreateContext(), sizedCache).LookupAsync("sizecap0");
        Assert.NotNull(second);
    }
}
