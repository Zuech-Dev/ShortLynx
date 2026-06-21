using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Domains;

namespace ShortLynx.Tests.Services.Domains;

public class CustomDomainServiceTests
{
    private sealed class FakeDnsResolver : IDnsResolver
    {
        public readonly Dictionary<string, List<string>> Records = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(
                Records.TryGetValue(name, out var r) ? r : []);
    }

    private static readonly CustomDomainOptions Opts = new();

    private static CustomDomainService MakeSvc(ShortLynx.Data.Context.ShortLynxDbContext ctx, IDnsResolver dns)
        => new(ctx, dns, Options.Create(Opts));

    private static async Task<Guid> SeedAccountAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        await using var ctx = db.CreateContext();
        ctx.AccountEntities.Add(account);
        await ctx.SaveChangesAsync();
        return account.Id;
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_CreatesPendingDomain_WithToken()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);

        var domain = await MakeSvc(db.CreateContext(), new FakeDnsResolver())
            .AddAsync("go.example.com", uid);

        Assert.Equal("go.example.com", domain.Domain);
        Assert.Equal(DomainVerificationStatus.Pending, domain.VerificationStatus);
        Assert.False(domain.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(domain.VerificationToken));
    }

    [Theory]
    [InlineData("GO.Example.com", "go.example.com")]
    [InlineData("https://go.example.com/path", "go.example.com")]
    [InlineData("  go.example.com.  ", "go.example.com")]
    public async Task Add_NormalisesDomain(string input, string expected)
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);

        var domain = await MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync(input, uid);

        Assert.Equal(expected, domain.Domain);
    }

    [Fact]
    public async Task Add_DuplicateDomain_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);

        await MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync("dupe.example.com", uid);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync("dupe.example.com", uid));
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsOnlyOwnersDomains()
    {
        await using var db = await TestDatabase.CreateAsync();
        var owner = await SeedAccountAsync(db);
        var other = await SeedAccountAsync(db);

        await MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync("mine.example.com", owner);
        await MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync("theirs.example.com", other);

        var mine = await MakeSvc(db.CreateContext(), new FakeDnsResolver()).ListAsync(owner);

        Assert.Single(mine);
        Assert.Equal("mine.example.com", mine[0].Domain);
    }

    // ── Verify ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_MatchingTxtRecord_SetsVerifiedAndActive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);
        var dns = new FakeDnsResolver();

        var domain = await MakeSvc(db.CreateContext(), dns).AddAsync("go.example.com", uid);
        dns.Records[Opts.VerificationHost("go.example.com")] = [Opts.ExpectedTxtValue(domain.VerificationToken)];

        var result = await MakeSvc(db.CreateContext(), dns).VerifyAsync(domain.Id, uid);

        Assert.NotNull(result);
        Assert.Equal(DomainVerificationStatus.Verified, result.VerificationStatus);
        Assert.True(result.IsActive);
        Assert.NotNull(result.VerifiedAt);
    }

    [Fact]
    public async Task Verify_NoMatchingRecord_SetsFailed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);
        var dns = new FakeDnsResolver();

        var domain = await MakeSvc(db.CreateContext(), dns).AddAsync("go.example.com", uid);
        dns.Records[Opts.VerificationHost("go.example.com")] = ["some-other-value"];

        var result = await MakeSvc(db.CreateContext(), dns).VerifyAsync(domain.Id, uid);

        Assert.NotNull(result);
        Assert.Equal(DomainVerificationStatus.Failed, result.VerificationStatus);
        Assert.False(result.IsActive);
        Assert.Null(result.VerifiedAt);
    }

    [Fact]
    public async Task Verify_DomainNotOwnedByUser_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var owner = await SeedAccountAsync(db);
        var other = await SeedAccountAsync(db);
        var dns = new FakeDnsResolver();

        var domain = await MakeSvc(db.CreateContext(), dns).AddAsync("go.example.com", owner);

        var result = await MakeSvc(db.CreateContext(), dns).VerifyAsync(domain.Id, other);

        Assert.Null(result);
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_OwnedDomain_DeletesAndReturnsTrue()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);
        var domain = await MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync("go.example.com", uid);

        var removed = await MakeSvc(db.CreateContext(), new FakeDnsResolver()).RemoveAsync(domain.Id, uid);
        Assert.True(removed);

        await using var ctx = db.CreateContext();
        Assert.False(await ctx.CustomDomainEntities.AnyAsync(d => d.Id == domain.Id));
    }

    [Fact]
    public async Task Remove_AnotherUsersDomain_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var owner = await SeedAccountAsync(db);
        var other = await SeedAccountAsync(db);
        var domain = await MakeSvc(db.CreateContext(), new FakeDnsResolver()).AddAsync("go.example.com", owner);

        var removed = await MakeSvc(db.CreateContext(), new FakeDnsResolver()).RemoveAsync(domain.Id, other);
        Assert.False(removed);
    }

    // ── RecheckVerifiedAsync (background re-verification) ──────────────────────

    private static async Task<ShortLynx.Data.Entities.CustomDomainEntity> SeedVerifiedAsync(
        TestDatabase db, IDnsResolver dns, Guid uid, string domain)
    {
        var added = await MakeSvc(db.CreateContext(), dns).AddAsync(domain, uid);
        await using var ctx = db.CreateContext();
        var stored = await ctx.CustomDomainEntities.FindAsync(added.Id);
        stored!.VerificationStatus = DomainVerificationStatus.Verified;
        stored.IsActive = true;
        stored.VerifiedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();
        return stored;
    }

    [Fact]
    public async Task Recheck_VerifiedDomainWithMissingTxt_DemotesToFailedAndInactive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);
        var dns = new FakeDnsResolver(); // publishes nothing
        var domain = await SeedVerifiedAsync(db, dns, uid, "go.example.com");

        var demoted = await MakeSvc(db.CreateContext(), dns).RecheckVerifiedAsync();
        Assert.Equal(1, demoted);

        await using var ctx = db.CreateContext();
        var stored = await ctx.CustomDomainEntities.FindAsync(domain.Id);
        Assert.Equal(DomainVerificationStatus.Failed, stored!.VerificationStatus);
        Assert.False(stored.IsActive);
        Assert.Null(stored.VerifiedAt);
    }

    [Fact]
    public async Task Recheck_VerifiedDomainStillPublishingTxt_StaysVerified()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);
        var dns = new FakeDnsResolver();
        var domain = await SeedVerifiedAsync(db, dns, uid, "go.example.com");
        dns.Records[Opts.VerificationHost("go.example.com")] = [Opts.ExpectedTxtValue(domain.VerificationToken)];

        var demoted = await MakeSvc(db.CreateContext(), dns).RecheckVerifiedAsync();
        Assert.Equal(0, demoted);

        await using var ctx = db.CreateContext();
        var stored = await ctx.CustomDomainEntities.FindAsync(domain.Id);
        Assert.Equal(DomainVerificationStatus.Verified, stored!.VerificationStatus);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Recheck_IgnoresPendingDomains()
    {
        await using var db = await TestDatabase.CreateAsync();
        var uid = await SeedAccountAsync(db);
        var dns = new FakeDnsResolver();
        var added = await MakeSvc(db.CreateContext(), dns).AddAsync("pending.example.com", uid); // Pending

        var demoted = await MakeSvc(db.CreateContext(), dns).RecheckVerifiedAsync();
        Assert.Equal(0, demoted);

        await using var ctx = db.CreateContext();
        var stored = await ctx.CustomDomainEntities.FindAsync(added.Id);
        Assert.Equal(DomainVerificationStatus.Pending, stored!.VerificationStatus);
    }
}
