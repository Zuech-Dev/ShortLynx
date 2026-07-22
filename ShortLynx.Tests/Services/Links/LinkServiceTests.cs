using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Links;
using ShortLynx.Services.ShortCodes;

namespace ShortLynx.Tests.Services.Links;

public class LinkServiceTests
{
    private static LinkService MakeSvc(
        ShortLynx.Data.Context.ShortLynxDbContext ctx, bool urlValid = true, string? invalidReason = null,
        IEntitlements? entitlements = null)
        => new(ctx,
            new RandomBase62Generator(Options.Create(new ShortCodeOptions { Length = 8 })),
            new StubUrlValidationService(urlValid, invalidReason),
            entitlements ?? new UnlimitedEntitlements());

    private static async Task<Guid> SeedAccountAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        await using var ctx = db.CreateContext();
        ctx.AccountEntities.Add(account);
        await ctx.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<ApiKeyEntity> SeedApiKeyAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        var key = EntityFactory.ApiKey(account.Id);
        await using var ctx = db.CreateContext();
        ctx.AddRange(account, key);
        await ctx.SaveChangesAsync();
        return key;
    }

    private static async Task<LinkEntity> SeedLinkAsync(TestDatabase db, ApiKeyEntity key)
    {
        var link = EntityFactory.AnonymousLink(key.AccountId);
        await using var ctx = db.CreateContext();
        ctx.LinkEntities.Add(link);
        await ctx.SaveChangesAsync();
        return link;
    }

    // Denies everything — stands in for a hosted plan policy at its limit.
    private sealed class DenyEntitlements : IEntitlements
    {
        public Task<bool> CanCreateLinkAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CanCreateCustomCodeAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default) => Task.FromResult(false);
    }

    // ── Entitlements gate ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAnonymousLink_WhenLinkQuotaReached_ThrowsEntitlement()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var svc = MakeSvc(db.CreateContext(), entitlements: new DenyEntitlements());

        await Assert.ThrowsAsync<EntitlementException>(
            () => svc.CreateAnonymousLinkAsync("https://example.com", accountId));
    }

    [Fact]
    public async Task CreateUserAttributedLink_WhenFeatureNotInPlan_ThrowsEntitlement()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var svc = MakeSvc(db.CreateContext(), entitlements: new DenyEntitlements());

        await Assert.ThrowsAsync<EntitlementException>(
            () => svc.CreateUserAttributedLinkAsync("https://example.com", accountId));
    }

    // ── CreateAnonymousLinkAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAnonymousLink_SavesLinkAndShortcode()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);

        var result = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", key);

        await using var ctx = db.CreateContext();
        var link = await ctx.LinkEntities.FindAsync(result.Link.Id);
        var sc = await ctx.ShortCodeEntities.FindAsync(result.ShortCode.Id);

        Assert.NotNull(link);
        Assert.NotNull(sc);
        Assert.Equal("https://example.com", link.OriginalUrl);
        Assert.Equal(link.Id, sc.LinkId);
    }

    [Fact]
    public async Task CreateAnonymousLink_InvalidUrl_ThrowsArgumentException()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            MakeSvc(db.CreateContext(), urlValid: false, invalidReason: "blocked URL")
                .CreateAnonymousLinkAsync("https://bad.example.com", key));
    }

    [Fact]
    public async Task CreateAnonymousLink_ShortcodeIsBase62OfConfiguredLength()
    {
        const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);

        var result = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", key);

        Assert.Equal(8, result.ShortCode.Code.Length);
        Assert.All(result.ShortCode.Code, c => Assert.Contains(c, Base62Chars));
    }

    [Fact]
    public async Task CreateAnonymousLink_SetsOwnerApiKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);

        var result = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", key);

        Assert.Equal(key.Id, result.Link.ApiKeyId);
    }

    [Fact]
    public async Task CreateAnonymousLink_IsActive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);

        var result = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", key);

        Assert.True(result.Link.IsActive);
        Assert.True(result.ShortCode.IsActive);
    }

    // ── CreateAnonymousLinkAsync (user-owned / dashboard) ─────────────────────

    [Fact]
    public async Task CreateAccountOwnedLink_SetsAccountId_AndNullApiKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        var result = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", accountId);

        await using var ctx = db.CreateContext();
        var link = await ctx.LinkEntities.FindAsync(result.Link.Id);
        Assert.NotNull(link);
        Assert.Equal(accountId, link.AccountId);
        Assert.Null(link.ApiKeyId);
        Assert.Equal(8, result.ShortCode.Code.Length);
    }

    [Fact]
    public async Task CreateAccountOwnedLink_InvalidUrl_ThrowsArgumentException()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            MakeSvc(db.CreateContext(), urlValid: false, invalidReason: "blocked URL")
                .CreateAnonymousLinkAsync("https://bad.example.com", accountId));
    }

    // ── CreateUserLinkCodesAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateUserLinkCodes_CreatesOnePerUserId()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var userIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var results = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, userIds);

        Assert.Equal(3, results.Count);

        await using var ctx = db.CreateContext();
        var count = await ctx.UserLinkCodeEntities.Where(c => c.LinkId == link.Id).CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task CreateUserLinkCodes_IsIdempotent_ReturnsSameCodeForSameUser()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var userId = Guid.CreateVersion7();

        var first = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, [userId]);
        var second = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, [userId]);

        Assert.Equal(first[0].Code, second[0].Code);
    }

    [Fact]
    public async Task CreateUserLinkCodes_AllCodesHaveCorrectLinkId()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var userIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var results = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, userIds);

        Assert.All(results, r => Assert.Equal(link.Id, r.LinkId));
    }

    [Fact]
    public async Task CreateUserLinkCodes_EmptyUserList_ReturnsEmptyList()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var results = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, []);
        Assert.Empty(results);
    }

    [Fact]
    public async Task CreateUserLinkCodes_CodesAreBase62()
    {
        const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var results = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, [Guid.CreateVersion7()]);

        Assert.All(results[0].Code, c => Assert.Contains(c, Base62Chars));
    }

    // ── CreateUserAttributedLinkAsync (Mode 2, dashboard) ─────────────────────

    [Fact]
    public async Task CreateUserAttributedLink_SetsModeAndOwner_AndMintsNoShortCode()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        var link = await MakeSvc(db.CreateContext()).CreateUserAttributedLinkAsync("https://example.com", accountId);

        await using var ctx = db.CreateContext();
        var stored = await ctx.LinkEntities.FindAsync(link.Id);
        Assert.NotNull(stored);
        Assert.Equal(LinkMode.UserAttributed, stored.Mode);
        Assert.Equal(accountId, stored.AccountId);
        Assert.Null(stored.ApiKeyId);

        // Mode-2 links resolve only via user codes — no anonymous short code is created.
        var shortCodes = await ctx.ShortCodeEntities.CountAsync(sc => sc.LinkId == link.Id);
        Assert.Equal(0, shortCodes);
    }

    [Fact]
    public async Task CreateUserAttributedLink_InvalidUrl_ThrowsArgumentException()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            MakeSvc(db.CreateContext(), urlValid: false, invalidReason: "blocked URL")
                .CreateUserAttributedLinkAsync("https://bad.example.com", accountId));
    }

    // ── CreateUserLinkCodesAsync (recipient labels + one-time use) ────────────

    [Fact]
    public async Task CreateUserLinkCodes_WithRecipients_StoresLabelAndOneTimeFlag()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var recipients = new[]
        {
            new CodeRecipient(Guid.CreateVersion7(), "alice@example.com"),
            new CodeRecipient(Guid.CreateVersion7(), "bob@example.com"),
        };

        var results = await MakeSvc(db.CreateContext())
            .CreateUserLinkCodesAsync(link.Id, recipients, isOneTimeUse: true);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsOneTimeUse));
        Assert.Contains(results, r => r.Recipient == "alice@example.com");
        Assert.Contains(results, r => r.Recipient == "bob@example.com");
    }

    [Fact]
    public async Task CreateUserLinkCodes_DedupesByRecipientLabel_AcrossCalls()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        // Same label, different fresh UserId each call (mirrors the dashboard pasting the same list twice).
        var first = await MakeSvc(db.CreateContext())
            .CreateUserLinkCodesAsync(link.Id, [new CodeRecipient(Guid.CreateVersion7(), "carol@example.com")], isOneTimeUse: false);
        var second = await MakeSvc(db.CreateContext())
            .CreateUserLinkCodesAsync(link.Id, [new CodeRecipient(Guid.CreateVersion7(), "carol@example.com")], isOneTimeUse: false);

        Assert.Equal(first[0].Code, second[0].Code);

        await using var ctx = db.CreateContext();
        var count = await ctx.UserLinkCodeEntities.CountAsync(c => c.LinkId == link.Id && c.Recipient == "carol@example.com");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateUserLinkCodes_GuidOverload_LeavesRecipientNullAndNotOneTime()
    {
        await using var db = await TestDatabase.CreateAsync();
        var key = await SeedApiKeyAsync(db);
        var link = await SeedLinkAsync(db, key);

        var results = await MakeSvc(db.CreateContext()).CreateUserLinkCodesAsync(link.Id, [Guid.CreateVersion7()]);

        Assert.Single(results);
        Assert.Null(results[0].Recipient);
        Assert.False(results[0].IsOneTimeUse);
    }

    // ── SetLinkDomainAsync (custom-domain pinning) ────────────────────────────

    private static async Task<Guid> SeedVerifiedDomainAsync(TestDatabase db, Guid accountId, string domain = "go.example.com")
    {
        var d = EntityFactory.CustomDomain(accountId, domain);
        d.VerificationStatus = DomainVerificationStatus.Verified;
        d.IsActive = true;
        await using var ctx = db.CreateContext();
        ctx.CustomDomainEntities.Add(d);
        await ctx.SaveChangesAsync();
        return d.Id;
    }

    [Fact]
    public async Task SetLinkDomain_PinsToOwnVerifiedDomain_ReturnsTrue()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var link = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", accountId);
        var domainId = await SeedVerifiedDomainAsync(db, accountId);

        var ok = await MakeSvc(db.CreateContext()).SetLinkDomainAsync(link.Link.Id, domainId, accountId);
        Assert.True(ok);

        await using var ctx = db.CreateContext();
        var stored = await ctx.LinkEntities.FindAsync(link.Link.Id);
        Assert.Equal(domainId, stored!.CustomDomainId);
    }

    [Fact]
    public async Task SetLinkDomain_Null_Unpins()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var link = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", accountId);
        var domainId = await SeedVerifiedDomainAsync(db, accountId);
        await MakeSvc(db.CreateContext()).SetLinkDomainAsync(link.Link.Id, domainId, accountId);

        var ok = await MakeSvc(db.CreateContext()).SetLinkDomainAsync(link.Link.Id, null, accountId);
        Assert.True(ok);

        await using var ctx = db.CreateContext();
        var stored = await ctx.LinkEntities.FindAsync(link.Link.Id);
        Assert.Null(stored!.CustomDomainId);
    }

    [Fact]
    public async Task SetLinkDomain_UnverifiedDomain_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var link = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", accountId);

        // Pending (unverified) domain.
        Guid pendingId;
        await using (var ctx = db.CreateContext())
        {
            var d = EntityFactory.CustomDomain(accountId, "pending.example.com");
            ctx.CustomDomainEntities.Add(d);
            await ctx.SaveChangesAsync();
            pendingId = d.Id;
        }

        var ok = await MakeSvc(db.CreateContext()).SetLinkDomainAsync(link.Link.Id, pendingId, accountId);
        Assert.False(ok);
    }

    [Fact]
    public async Task SetLinkDomain_AnotherAccountsLink_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var ownerAccount = await SeedAccountAsync(db);
        var link = await MakeSvc(db.CreateContext()).CreateAnonymousLinkAsync("https://example.com", ownerAccount);

        var otherAccount = await SeedAccountAsync(db);
        var otherDomain = await SeedVerifiedDomainAsync(db, otherAccount, "other.example.com");

        var ok = await MakeSvc(db.CreateContext()).SetLinkDomainAsync(link.Link.Id, otherDomain, otherAccount);
        Assert.False(ok);
    }
}
