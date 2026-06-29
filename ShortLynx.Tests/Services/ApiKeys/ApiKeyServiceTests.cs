using Microsoft.Extensions.Options;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Services.ApiKeys;

public class ApiKeyServiceTests
{
    private const string Secret = "test-secret-at-least-32-chars-long!";

    private static ApiKeyService MakeSvc(ShortLynx.Data.Context.ShortLynxDbContext ctx, string secret = Secret)
        => new(ctx, Options.Create(new ApiKeyOptions { HmacSecret = secret }));

    private static async Task<Guid> SeedAccountAsync(TestDatabase db, string name = "Acct")
    {
        var account = EntityFactory.Account(name);
        await using var ctx = db.CreateContext();
        ctx.AccountEntities.Add(account);
        await ctx.SaveChangesAsync();
        return account.Id;
    }

    // Seeds an account and mints a key against it.
    private static async Task<(ApiKeyEntity Record, string Plaintext, Guid AccountId)> CreateKeyAsync(
        TestDatabase db, string name, string[] scopes, string secret = Secret)
    {
        var accountId = await SeedAccountAsync(db);
        var (record, plaintext) = await MakeSvc(db.CreateContext(), secret).CreateAsync(name, scopes, accountId);
        return (record, plaintext, accountId);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsBothRecordAndPlaintextKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext, _) = await CreateKeyAsync(db, "My Key", ["links:write"]);

        Assert.NotNull(record);
        Assert.Equal(64, plaintext.Length); // 32 random bytes → hex
    }

    [Fact]
    public async Task Create_PersistsEntityToDb()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _, _) = await CreateKeyAsync(db, "Persisted Key", []);

        await using var ctx = db.CreateContext();
        var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
        Assert.NotNull(stored);
        Assert.Equal("Persisted Key", stored.Name);
    }

    [Fact]
    public async Task Create_DoesNotStorePlaintextInDb()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext, _) = await CreateKeyAsync(db, "Hidden Key", []);

        Assert.NotEqual(plaintext, record.KeyHash);
        Assert.Equal(plaintext[..8], record.Prefix);
    }

    [Fact]
    public async Task Create_SetsIsActiveTrue()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _, _) = await CreateKeyAsync(db, "Active Key", []);
        Assert.True(record.IsActive);
    }

    [Fact]
    public async Task Create_JoinsMultipleScopesWithComma()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _, _) = await CreateKeyAsync(db, "Scoped Key", ["links:read", "links:write"]);
        Assert.Equal("links:read,links:write", record.Scopes);
    }

    [Fact]
    public async Task Create_SetsOwningAccount()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _, accountId) = await CreateKeyAsync(db, "Owned Key", ["links:read"]);
        Assert.Equal(accountId, record.AccountId);
    }

    [Fact]
    public async Task Create_WithUnknownAccount_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var svc = MakeSvc(db.CreateContext());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("Orphan Key", [], Guid.CreateVersion7()));
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_CorrectKey_ReturnsEntity()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, plaintext, _) = await CreateKeyAsync(db, "Valid Key", []);

        // Fresh context — simulates a new request.
        var result = await MakeSvc(db.CreateContext()).ValidateAsync(plaintext);

        Assert.NotNull(result);
        Assert.Equal("Valid Key", result.Name);
    }

    [Fact]
    public async Task Validate_WrongKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, plaintext, _) = await CreateKeyAsync(db, "Real Key", []);

        // Flip one character in the body of the key (not the prefix so lookup still fires).
        var tampered = plaintext[..10] + (plaintext[10] == 'A' ? 'B' : 'A') + plaintext[11..];
        var result = await MakeSvc(db.CreateContext()).ValidateAsync(tampered);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_InactiveKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext, _) = await CreateKeyAsync(db, "Inactive Key", []);

        await using (var ctx = db.CreateContext())
        {
            var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
            stored!.IsActive = false;
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).ValidateAsync(plaintext);
        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_ExpiredKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext, _) = await CreateKeyAsync(db, "Expired Key", []);

        await using (var ctx = db.CreateContext())
        {
            var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
            stored!.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).ValidateAsync(plaintext);
        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_TooShortKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var result = await MakeSvc(db.CreateContext()).ValidateAsync("short");
        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_EmptyString_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var result = await MakeSvc(db.CreateContext()).ValidateAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_KeyFromDifferentSecret_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, plaintext, _) = await CreateKeyAsync(db, "Key A", [], "secret-one-xxxxxxxxxxxxxxxxxxxxxxxxxx");

        var result = await MakeSvc(db.CreateContext(), "secret-two-xxxxxxxxxxxxxxxxxxxxxxxxxx").ValidateAsync(plaintext);
        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_TwoActiveKeysShareAPrefix_EachValidatesToItsOwnKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        // Two distinct plaintext keys that happen to share the same 8-char prefix (a collision).
        const string plaintextA = "COLLIDE0" + "AAAAAAAAAAAAAAAA";
        const string plaintextB = "COLLIDE0" + "BBBBBBBBBBBBBBBB";

        await using (var seed = db.CreateContext())
        {
            seed.ApiKeyEntities.Add(MakeKey("Key A", "COLLIDE0", Hmac(Secret, plaintextA), accountId));
            seed.ApiKeyEntities.Add(MakeKey("Key B", "COLLIDE0", Hmac(Secret, plaintextB), accountId));
            await seed.SaveChangesAsync();
        }

        var resultA = await MakeSvc(db.CreateContext()).ValidateAsync(plaintextA);
        var resultB = await MakeSvc(db.CreateContext()).ValidateAsync(plaintextB);

        Assert.NotNull(resultA);
        Assert.Equal("Key A", resultA.Name);
        Assert.NotNull(resultB);
        Assert.Equal("Key B", resultB.Name);
    }

    private static ApiKeyEntity MakeKey(string name, string prefix, string keyHash, Guid accountId) => new()
    {
        Id = Guid.CreateVersion7(),
        Prefix = prefix,
        KeyHash = keyHash,
        Name = name,
        Scopes = "links:read",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        AccountId = accountId,
    };

    private static string Hmac(string secret, string input) => Convert.ToHexString(
        System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(input)));

    // ── RevokeAsync (account-scoped) ──────────────────────────────────────────

    [Fact]
    public async Task Revoke_OwnActiveKey_DeactivatesAndBlocksAuth()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext, accountId) = await CreateKeyAsync(db, "k", ["links:read"]);

        var revoked = await MakeSvc(db.CreateContext()).RevokeAsync(record.Id, accountId);
        Assert.True(revoked);

        await using var ctx = db.CreateContext();
        var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
        Assert.False(stored!.IsActive);

        // A revoked key can no longer authenticate.
        var validated = await MakeSvc(db.CreateContext()).ValidateAsync(plaintext);
        Assert.Null(validated);
    }

    [Fact]
    public async Task Revoke_AnotherAccountsKey_ReturnsFalse_LeavesActive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _, _) = await CreateKeyAsync(db, "k", []);
        var otherAccountId = await SeedAccountAsync(db, "Other");

        var revoked = await MakeSvc(db.CreateContext()).RevokeAsync(record.Id, otherAccountId);
        Assert.False(revoked);

        await using var ctx = db.CreateContext();
        var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
        Assert.True(stored!.IsActive);
    }

    [Fact]
    public async Task Revoke_UnknownKey_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var revoked = await MakeSvc(db.CreateContext()).RevokeAsync(Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.False(revoked);
    }
}
