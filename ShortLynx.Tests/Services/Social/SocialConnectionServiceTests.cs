using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Social;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Social;

public class SocialConnectionServiceTests
{
    // Reversible fake so tests can assert both "ciphertext stored" and "round-trips".
    private sealed class FakeProtector : ITokenProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    private sealed class FakeConnector : ISocialConnector
    {
        public SocialPlatform Platform => SocialPlatform.Bluesky;
        public SocialIdentity Identity = new("did:plc:abc", "me.bsky.social", "access-1", "refresh-1", null);
        public int Calls;

        public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Identity);
        }

        public Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
            => Task.FromResult<SocialTokens?>(null);

        public Task<SocialPostMetrics?> GetPostMetricsAsync(SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
            => Task.FromResult<SocialPostMetrics?>(null);
    }

    private sealed class DenyEntitlements : IEntitlements
    {
        public Task<bool> CanCreateLinkAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CanCreateCustomCodeAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static SocialConnectionService MakeSvc(
        ShortLynxDbContext ctx, FakeConnector? connector = null, IEntitlements? entitlements = null)
        => new(ctx, [connector ?? new FakeConnector()], new FakeProtector(),
            entitlements ?? new UnlimitedEntitlements());

    private static async Task<Guid> SeedAccountAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        await using var ctx = db.CreateContext();
        ctx.AccountEntities.Add(account);
        await ctx.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task Connect_StoresEncryptedTokens_NeverPlaintext()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        var connection = await MakeSvc(db.CreateContext()).ConnectAsync(
            accountId, null, SocialPlatform.Bluesky, new SocialCredentials("me.bsky.social", "pw"));

        Assert.Equal("did:plc:abc", connection.ExternalAccountId);
        Assert.Equal("me.bsky.social", connection.Handle);
        // Stored form is ciphertext; the plaintext token appears nowhere in the row.
        Assert.Equal("enc:access-1", connection.AccessTokenProtected);
        Assert.Equal("enc:refresh-1", connection.RefreshTokenProtected);
        Assert.DoesNotContain("access-1", connection.AccessTokenProtected.Replace("enc:access-1", ""));
    }

    [Fact]
    public async Task Connect_SameExternalAccount_Upserts_NotDuplicates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var connector = new FakeConnector();

        var first = await MakeSvc(db.CreateContext(), connector).ConnectAsync(
            accountId, null, SocialPlatform.Bluesky, new SocialCredentials("me.bsky.social", "pw"));

        // Reconnect: same DID, new handle + tokens.
        connector.Identity = new SocialIdentity("did:plc:abc", "renamed.bsky.social", "access-2", "refresh-2", null);
        var second = await MakeSvc(db.CreateContext(), connector).ConnectAsync(
            accountId, null, SocialPlatform.Bluesky, new SocialCredentials("renamed.bsky.social", "pw"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("renamed.bsky.social", second.Handle);
        Assert.Equal("enc:access-2", second.AccessTokenProtected);

        await using var verify = db.CreateContext();
        Assert.Equal(1, await verify.SocialConnectionEntities.CountAsync());
    }

    [Fact]
    public async Task Connect_UnsupportedPlatform_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        // Only a Bluesky connector is registered — Mastodon must fail loudly, not silently no-op.
        await Assert.ThrowsAsync<ArgumentException>(() => MakeSvc(db.CreateContext()).ConnectAsync(
            accountId, null, SocialPlatform.Mastodon, new SocialCredentials("x", "y")));
    }

    [Fact]
    public async Task Connect_WhenPlanDenies_ThrowsEntitlement_WithoutCallingPlatform()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var connector = new FakeConnector();

        await Assert.ThrowsAsync<EntitlementException>(() =>
            MakeSvc(db.CreateContext(), connector, new DenyEntitlements()).ConnectAsync(
                accountId, null, SocialPlatform.Bluesky, new SocialCredentials("x", "y")));

        Assert.Equal(0, connector.Calls); // gate fires before any network call
    }

    [Fact]
    public async Task ConnectFromIdentity_StoresEncryptedTokens_NoCredentialInvolved()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var identity = new SocialIdentity("17800000000000000", "@me", "long-lived-token", null, DateTimeOffset.UtcNow.AddDays(60));

        // The OAuth path (Threads): no SocialCredentials — a verified identity is handed straight in.
        var connection = await MakeSvc(db.CreateContext()).ConnectFromIdentityAsync(
            accountId, null, SocialPlatform.Threads, identity);

        Assert.Equal("17800000000000000", connection.ExternalAccountId);
        Assert.Equal("@me", connection.Handle);
        Assert.Equal("enc:long-lived-token", connection.AccessTokenProtected);
        Assert.Null(connection.RefreshTokenProtected);
    }

    [Fact]
    public async Task ConnectFromIdentity_SameExternalAccount_Upserts_NotDuplicates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var svc = MakeSvc(db.CreateContext());

        var first = await svc.ConnectFromIdentityAsync(accountId, null, SocialPlatform.Threads,
            new SocialIdentity("178", "@me", "token-1", null, null));
        var second = await svc.ConnectFromIdentityAsync(accountId, null, SocialPlatform.Threads,
            new SocialIdentity("178", "@renamed", "token-2", null, null));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("@renamed", second.Handle);
        Assert.Equal("enc:token-2", second.AccessTokenProtected);

        await using var verify = db.CreateContext();
        Assert.Equal(1, await verify.SocialConnectionEntities.CountAsync());
    }

    [Fact]
    public async Task ConnectFromIdentity_WhenPlanDenies_ThrowsEntitlement_NoConnectionPersisted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        await Assert.ThrowsAsync<EntitlementException>(() =>
            MakeSvc(db.CreateContext(), entitlements: new DenyEntitlements()).ConnectFromIdentityAsync(
                accountId, null, SocialPlatform.Threads, new SocialIdentity("178", "@me", "token", null, null)));

        await using var verify = db.CreateContext();
        Assert.Equal(0, await verify.SocialConnectionEntities.CountAsync());
    }

    [Fact]
    public async Task List_IsAccountScoped_And_Disconnect_EnforcesOwnership()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);

        var connection = await MakeSvc(db.CreateContext()).ConnectAsync(
            a, null, SocialPlatform.Bluesky, new SocialCredentials("me.bsky.social", "pw"));

        Assert.Single(await MakeSvc(db.CreateContext()).ListAsync(a));
        Assert.Empty(await MakeSvc(db.CreateContext()).ListAsync(b));

        // Another account can't disconnect it; the owner can.
        Assert.False(await MakeSvc(db.CreateContext()).DisconnectAsync(connection.Id, b));
        Assert.True(await MakeSvc(db.CreateContext()).DisconnectAsync(connection.Id, a));
        Assert.Empty(await MakeSvc(db.CreateContext()).ListAsync(a));
    }
}
