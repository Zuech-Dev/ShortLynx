using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Enums;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Data;

public class EntityConstraintTests
{
    // ── ShortCode ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShortCode_Code_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var key = EntityFactory.ApiKey();
        var link1 = EntityFactory.AnonymousLink(key.Id);
        var link2 = EntityFactory.AnonymousLink(key.Id);
        ctx.AddRange(key, link1, link2);
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.ShortCode(link1.Id, "abc123"));
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.ShortCode(link2.Id, "abc123")); // duplicate code
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task ShortCode_LinkId_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var key = EntityFactory.ApiKey();
            var link = EntityFactory.AnonymousLink(key.Id);
            ctx.AddRange(key, link, EntityFactory.ShortCode(link.Id, "aaa111"));
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        // Fresh context has no tracked entities — second insert reaches the DB unique index
        await using var ctx2 = db.CreateContext();
        ctx2.Add(EntityFactory.ShortCode(linkId, "bbb222")); // same link
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    // ── UserLinkCode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UserLinkCode_Code_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var key = EntityFactory.ApiKey();
        var link = EntityFactory.AnonymousLink(key.Id);
        ctx.AddRange(key, link);
        await ctx.SaveChangesAsync();

        var userId1 = Guid.CreateVersion7();
        var userId2 = Guid.CreateVersion7();
        ctx.Add(EntityFactory.UserLinkCode(link.Id, userId1, "xyz999"));
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.UserLinkCode(link.Id, userId2, "xyz999")); // duplicate code
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task UserLinkCode_LinkAndUser_CompositeKey_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var key = EntityFactory.ApiKey();
        var link = EntityFactory.AnonymousLink(key.Id);
        ctx.AddRange(key, link);
        await ctx.SaveChangesAsync();

        var userId = Guid.CreateVersion7();
        ctx.Add(EntityFactory.UserLinkCode(link.Id, userId, "code1"));
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.UserLinkCode(link.Id, userId, "code2")); // same (link, user)
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ── UserAccount ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UserAccount_Email_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        ctx.Add(EntityFactory.UserAccount("alice@example.com"));
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.UserAccount("alice@example.com")); // duplicate email
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task UserAccount_Delete_CascadesTo_MagicLinkTokens()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid userId;
        Guid tokenId;

        await using (var ctx = db.CreateContext())
        {
            var user = EntityFactory.UserAccount();
            var token = EntityFactory.MagicLinkToken(user.Id);
            ctx.AddRange(user, token);
            await ctx.SaveChangesAsync();
            userId = user.Id;
            tokenId = token.Id;
        }

        // Delete user in a separate context (tokens not tracked — relies on DB cascade)
        await using (var ctx = db.CreateContext())
        {
            var user = await ctx.UserAccountEntities.FindAsync(userId);
            ctx.Remove(user!);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            var token = await ctx.MagicLinkTokenEntities.FindAsync(tokenId);
            Assert.Null(token);
        }
    }

    [Fact]
    public async Task UserAccount_Delete_CascadesTo_CustomDomains()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid userId;
        Guid domainId;

        await using (var ctx = db.CreateContext())
        {
            var user = EntityFactory.UserAccount();
            var domain = EntityFactory.CustomDomain(user.Id);
            ctx.AddRange(user, domain);
            await ctx.SaveChangesAsync();
            userId = user.Id;
            domainId = domain.Id;
        }

        await using (var ctx = db.CreateContext())
        {
            var user = await ctx.UserAccountEntities.FindAsync(userId);
            ctx.Remove(user!);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
        {
            var domain = await ctx.CustomDomainEntities.FindAsync(domainId);
            Assert.Null(domain);
        }
    }

    // ── CustomDomain ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CustomDomain_Domain_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var user1 = EntityFactory.UserAccount("u1@example.com");
        var user2 = EntityFactory.UserAccount("u2@example.com");
        ctx.AddRange(user1, user2);
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.CustomDomain(user1.Id, "go.acme.com"));
        await ctx.SaveChangesAsync();

        ctx.Add(EntityFactory.CustomDomain(user2.Id, "go.acme.com")); // same domain
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ── Enum storage ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DomainVerificationStatus_RoundTrips_AsInt()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid domainId;
        await using (var ctx = db.CreateContext())
        {
            var user = EntityFactory.UserAccount();
            var domain = EntityFactory.CustomDomain(user.Id);
            domain.VerificationStatus = DomainVerificationStatus.Verified;
            domain.VerifiedAt = DateTimeOffset.UtcNow;
            ctx.AddRange(user, domain);
            await ctx.SaveChangesAsync();
            domainId = domain.Id;
        }

        await using (var ctx = db.CreateContext())
        {
            var domain = await ctx.CustomDomainEntities.FindAsync(domainId);
            Assert.Equal(DomainVerificationStatus.Verified, domain!.VerificationStatus);
            Assert.NotNull(domain.VerifiedAt);
        }
    }

    [Fact]
    public async Task LinkMode_RoundTrips_AsInt()
    {
        await using var db = await TestDatabase.CreateAsync();

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var key = EntityFactory.ApiKey();
            var link = EntityFactory.AnonymousLink(key.Id);
            link.Mode = LinkMode.UserAttributed;
            ctx.AddRange(key, link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        await using (var ctx = db.CreateContext())
        {
            var link = await ctx.LinkEntities.FindAsync(linkId);
            Assert.Equal(LinkMode.UserAttributed, link!.Mode);
        }
    }

    // ── ApiKey ↔ UserAccount (optional FK) ────────────────────────────────────

    [Fact]
    public async Task ApiKey_UserAccountId_CanBeNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var key = EntityFactory.ApiKey();
        // UserAccountId not set → remains null
        ctx.Add(key);
        await ctx.SaveChangesAsync(); // must not throw

        var saved = await ctx.ApiKeyEntities.FindAsync(key.Id);
        Assert.Null(saved!.UserAccountId);
    }

    [Fact]
    public async Task ApiKey_CanReference_UserAccount()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();

        var user = EntityFactory.UserAccount();
        var key = EntityFactory.ApiKey();
        key.UserAccountId = user.Id;
        ctx.AddRange(user, key);
        await ctx.SaveChangesAsync();

        var saved = await ctx.ApiKeyEntities.FindAsync(key.Id);
        Assert.Equal(user.Id, saved!.UserAccountId);
    }
}
