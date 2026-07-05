using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Social;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Social;

public class SocialPublishServiceTests
{
    private sealed class FakeProtector : ITokenProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    // Scriptable connector: can expire the first N publish attempts, refuse content, or refresh.
    private sealed class ScriptedConnector : ISocialConnector
    {
        public SocialPlatform Platform => SocialPlatform.Bluesky;
        public int PublishCalls;
        public string? LastText;
        public SocialTokens? LastTokens;
        public int ExpireFirstN;
        public SocialTokens? RefreshResult;
        public Exception? PublishError;
        public string? PublishErrorHandle; // when set, PublishError only fires for this connection

        public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
        {
            PublishCalls++;
            LastText = text;
            LastTokens = connection.Tokens;
            if (PublishError is not null && (PublishErrorHandle is null || connection.Handle == PublishErrorHandle))
                throw PublishError;
            if (PublishCalls <= ExpireFirstN) throw new TokenExpiredException("expired");
            return Task.FromResult(new SocialPostRef($"post-{PublishCalls}", $"https://bsky.app/post/{PublishCalls}"));
        }

        public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
            => Task.FromResult(RefreshResult);

        public Task<SocialPostMetrics?> GetPostMetricsAsync(SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
            => Task.FromResult<SocialPostMetrics?>(null);
    }

    private static SocialPublishService MakeSvc(ShortLynxDbContext ctx, ScriptedConnector connector)
        => new(ctx, [connector], new FakeProtector(), new UnlimitedEntitlements());

    private static async Task<(Guid AccountId, Guid LinkId, Guid ConnectionId)> SeedAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var connection = new SocialConnectionEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            Platform = SocialPlatform.Bluesky,
            ExternalAccountId = "did:plc:abc",
            Handle = "me.bsky.social",
            AccessTokenProtected = "enc:access-1",
            RefreshTokenProtected = "enc:refresh-1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var ctx = db.CreateContext();
        ctx.AddRange(account, link, connection);
        await ctx.SaveChangesAsync();
        return (account.Id, link.Id, connection.Id);
    }

    [Fact]
    public async Task Publish_Success_RecordsPost_WithComposedText_AndDecryptedTokens()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, connectionId) = await SeedAsync(db);
        var connector = new ScriptedConnector();

        var results = await MakeSvc(db.CreateContext(), connector)
            .PublishLinkAsync(accountId, linkId, [connectionId], "New post!", "https://s.example/abc");

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("https://bsky.app/post/1", result.Post!.PostUrl);
        Assert.Equal("New post!\n\nhttps://s.example/abc", connector.LastText);
        Assert.Equal("access-1", connector.LastTokens!.AccessToken); // decrypted before the connector sees it

        await using var verify = db.CreateContext();
        var post = await verify.SocialPostEntities.SingleAsync();
        Assert.Equal(linkId, post.LinkId);
        Assert.Equal("me.bsky.social", post.Handle);
        Assert.Null(post.Impressions); // metrics come from the (future) puller
    }

    [Fact]
    public async Task Publish_ExpiredToken_Refreshes_PersistsRotatedTokens_AndRetries()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, connectionId) = await SeedAsync(db);
        var connector = new ScriptedConnector
        {
            ExpireFirstN = 1,
            RefreshResult = new SocialTokens("access-2", "refresh-2"),
        };

        var results = await MakeSvc(db.CreateContext(), connector)
            .PublishLinkAsync(accountId, linkId, [connectionId], "hi", "https://s.example/abc");

        Assert.True(Assert.Single(results).Success);
        Assert.Equal(2, connector.PublishCalls);                     // failed once, retried once
        Assert.Equal("access-2", connector.LastTokens!.AccessToken); // retry used the fresh token

        await using var verify = db.CreateContext();
        var connection = await verify.SocialConnectionEntities.SingleAsync();
        Assert.Equal("enc:access-2", connection.AccessTokenProtected);   // rotation persisted, encrypted
        Assert.Equal("enc:refresh-2", connection.RefreshTokenProtected);
    }

    [Fact]
    public async Task Publish_ExpiredToken_NoRefreshAvailable_FailsWithReconnect_NoPost()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, connectionId) = await SeedAsync(db);
        var connector = new ScriptedConnector { ExpireFirstN = 99, RefreshResult = null };

        var results = await MakeSvc(db.CreateContext(), connector)
            .PublishLinkAsync(accountId, linkId, [connectionId], "hi", "https://s.example/abc");

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("reconnect", result.Error, StringComparison.OrdinalIgnoreCase);

        await using var verify = db.CreateContext();
        Assert.Equal(0, await verify.SocialPostEntities.CountAsync());
    }

    [Fact]
    public async Task Publish_OneConnectionRejected_OthersStillSucceed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, connectionId) = await SeedAsync(db);

        // Second connection on the same account; the connector is scripted to reject only this one.
        Guid badConnectionId;
        await using (var ctx = db.CreateContext())
        {
            var bad = new SocialConnectionEntity
            {
                Id = Guid.CreateVersion7(), AccountId = accountId, Platform = SocialPlatform.Bluesky,
                ExternalAccountId = "did:plc:bad", Handle = "bad.bsky.social",
                AccessTokenProtected = "enc:access-bad", CreatedAt = DateTimeOffset.UtcNow,
            };
            ctx.Add(bad);
            await ctx.SaveChangesAsync();
            badConnectionId = bad.Id;
        }

        var connector = new ScriptedConnector
        {
            PublishError = new ArgumentException("Post exceeds the platform's length limit."),
            PublishErrorHandle = "bad.bsky.social",
        };

        var results = await MakeSvc(db.CreateContext(), connector)
            .PublishLinkAsync(accountId, linkId, [connectionId, badConnectionId], "hi", "https://s.example/abc");

        // Partial failure is per-connection, never all-or-nothing.
        Assert.Equal(2, results.Count);
        var ok = Assert.Single(results, r => r.Success);
        var failed = Assert.Single(results, r => !r.Success);
        Assert.Equal("me.bsky.social", ok.Handle);
        Assert.Equal("bad.bsky.social", failed.Handle);
        Assert.Contains("length limit", failed.Error);

        await using var verify = db.CreateContext();
        Assert.Equal(1, await verify.SocialPostEntities.CountAsync()); // only the success recorded
    }

    [Fact]
    public async Task Publish_UnknownConnection_ReportsFailure_NotThrow()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, linkId, _) = await SeedAsync(db);

        var results = await MakeSvc(db.CreateContext(), new ScriptedConnector())
            .PublishLinkAsync(accountId, linkId, [Guid.CreateVersion7()], "hi", "https://s.example/abc");

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_ForeignLink_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, _, connectionId) = await SeedAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() => MakeSvc(db.CreateContext(), new ScriptedConnector())
            .PublishLinkAsync(accountId, Guid.CreateVersion7(), [connectionId], "hi", "https://s.example/abc"));
    }

    [Theory]
    [InlineData(null, "https://s.example/abc", "https://s.example/abc")]
    [InlineData("", "https://s.example/abc", "https://s.example/abc")]
    [InlineData("Read this!", "https://s.example/abc", "Read this!\n\nhttps://s.example/abc")]
    [InlineData("Already inline https://s.example/abc here", "https://s.example/abc", "Already inline https://s.example/abc here")]
    public void Compose_AppendsShortUrl_UnlessPresent(string? text, string url, string expected)
        => Assert.Equal(expected, SocialPublishService.Compose(text, url));
}
