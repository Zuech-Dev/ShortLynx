using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Social;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Social;

public class SocialMetricsServiceTests
{
    private sealed class FakeProtector : ITokenProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    private sealed class ScriptedConnector : ISocialConnector
    {
        public SocialPlatform Platform => SocialPlatform.Bluesky;
        public SocialPostMetrics? Metrics = new(null, 10, 3, 1);
        public int MetricsCalls;
        public int ExpireFirstN;
        public SocialTokens? RefreshResult;

        public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
            => Task.FromResult(RefreshResult);

        public Task<SocialPostMetrics?> GetPostMetricsAsync(SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
        {
            MetricsCalls++;
            if (MetricsCalls <= ExpireFirstN) throw new TokenExpiredException("expired");
            return Task.FromResult(Metrics);
        }
    }

    private static SocialMetricsService MakeSvc(ShortLynxDbContext ctx, ScriptedConnector connector,
        int intervalMinutes = 60, int windowDays = 14)
        => new(ctx, [connector], new FakeProtector(),
            Options.Create(new SocialMetricsOptions { RefreshIntervalMinutes = intervalMinutes, RefreshWindowDays = windowDays }));

    private static async Task<(Guid AccountId, Guid LinkId, Guid PostId)> SeedAsync(
        TestDatabase db, DateTimeOffset? postedAt = null, DateTimeOffset? metricsUpdatedAt = null,
        bool disconnected = false)
    {
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var connection = new SocialConnectionEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, Platform = SocialPlatform.Bluesky,
            ExternalAccountId = "did:plc:abc", Handle = "me.bsky.social",
            AccessTokenProtected = "enc:access-1", RefreshTokenProtected = "enc:refresh-1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var post = new SocialPostEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, LinkId = link.Id,
            SocialConnectionId = disconnected ? null : connection.Id,
            Platform = SocialPlatform.Bluesky, Handle = "me.bsky.social",
            ExternalPostId = "at://did:plc:abc/app.bsky.feed.post/rk1",
            Text = "hi", PostedAt = postedAt ?? DateTimeOffset.UtcNow.AddHours(-1),
            MetricsUpdatedAt = metricsUpdatedAt,
        };
        await using var ctx = db.CreateContext();
        ctx.AddRange(account, link, connection, post);
        await ctx.SaveChangesAsync();
        return (account.Id, link.Id, post.Id);
    }

    [Fact]
    public async Task RefreshLink_StoresEngagement_AndStampsUpdatedAt()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, postId) = await SeedAsync(db);

        var updated = await MakeSvc(db.CreateContext(), new ScriptedConnector()).RefreshLinkAsync(accountId, linkId);

        Assert.Equal(1, updated);
        await using var verify = db.CreateContext();
        var post = await verify.SocialPostEntities.SingleAsync(p => p.Id == postId);
        Assert.Equal(10, post.Likes);
        Assert.Equal(3, post.Reposts);
        Assert.Equal(1, post.Replies);
        Assert.Null(post.Impressions);          // Tier-A platforms expose no view counts
        Assert.NotNull(post.MetricsUpdatedAt);
    }

    [Fact]
    public async Task RefreshLink_ExpiredToken_RefreshesRotatesAndRetries()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, _) = await SeedAsync(db);
        var connector = new ScriptedConnector
        {
            ExpireFirstN = 1,
            RefreshResult = new SocialTokens("access-2", "refresh-2"),
        };

        var updated = await MakeSvc(db.CreateContext(), connector).RefreshLinkAsync(accountId, linkId);

        Assert.Equal(1, updated);
        Assert.Equal(2, connector.MetricsCalls);
        await using var verify = db.CreateContext();
        var connection = await verify.SocialConnectionEntities.SingleAsync();
        Assert.Equal("enc:access-2", connection.AccessTokenProtected); // rotation persisted, encrypted
    }

    [Fact]
    public async Task RefreshLink_PostDeletedOnPlatform_KeepsCounts_ButAdvancesTimestamp()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, postId) = await SeedAsync(db);
        var connector = new ScriptedConnector { Metrics = null };

        await MakeSvc(db.CreateContext(), connector).RefreshLinkAsync(accountId, linkId);

        await using var verify = db.CreateContext();
        var post = await verify.SocialPostEntities.SingleAsync(p => p.Id == postId);
        Assert.Null(post.Likes);                 // never had counts; not fabricated
        Assert.NotNull(post.MetricsUpdatedAt);   // won't be hammered every pass
    }

    [Fact]
    public async Task RefreshRecent_SkipsFresh_OutOfWindow_AndDisconnected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db);                                                     // due: 1h old, never pulled
        await SeedAsync(db, metricsUpdatedAt: now.AddMinutes(-5));               // fresh: pulled 5m ago (interval 60m)
        await SeedAsync(db, postedAt: now.AddDays(-30));                         // out of the 14-day window
        await SeedAsync(db, disconnected: true);                                 // no tokens to pull with
        var connector = new ScriptedConnector();

        var updated = await MakeSvc(db.CreateContext(), connector).RefreshRecentAsync();

        Assert.Equal(1, updated);
        Assert.Equal(1, connector.MetricsCalls);
    }
}
