using Microsoft.Extensions.Options;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Services.ApiKeys;

public class ApiKeyServiceTests
{
    private static ApiKeyService MakeSvc(ShortLynx.Data.Context.ShortLynxDbContext ctx, string secret = "test-secret-at-least-32-chars-long!")
        => new(ctx, Options.Create(new ApiKeyOptions { HmacSecret = secret }));

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsBothRecordAndPlaintextKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext) = await MakeSvc(db.CreateContext()).CreateAsync("My Key", ["links:write"]);

        Assert.NotNull(record);
        Assert.Equal(64, plaintext.Length); // 32 random bytes → hex
    }

    [Fact]
    public async Task Create_PersistsEntityToDb()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _) = await MakeSvc(db.CreateContext()).CreateAsync("Persisted Key", []);

        await using var ctx = db.CreateContext();
        var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
        Assert.NotNull(stored);
        Assert.Equal("Persisted Key", stored.Name);
    }

    [Fact]
    public async Task Create_DoesNotStorePlaintextInDb()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, plaintext) = await MakeSvc(db.CreateContext()).CreateAsync("Hidden Key", []);

        Assert.NotEqual(plaintext, record.KeyHash);
        Assert.Equal(plaintext[..8], record.Prefix);
    }

    [Fact]
    public async Task Create_SetsIsActiveTrue()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _) = await MakeSvc(db.CreateContext()).CreateAsync("Active Key", []);
        Assert.True(record.IsActive);
    }

    [Fact]
    public async Task Create_JoinsMultipleScopesWithComma()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (record, _) = await MakeSvc(db.CreateContext()).CreateAsync("Scoped Key", ["links:read", "links:write"]);
        Assert.Equal("links:read,links:write", record.Scopes);
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_CorrectKey_ReturnsEntity()
    {
        await using var db = await TestDatabase.CreateAsync();
        const string secret = "test-secret-at-least-32-chars-long!";
        var (_, plaintext) = await MakeSvc(db.CreateContext(), secret).CreateAsync("Valid Key", []);

        // Fresh context — simulates a new request.
        var result = await MakeSvc(db.CreateContext(), secret).ValidateAsync(plaintext);

        Assert.NotNull(result);
        Assert.Equal("Valid Key", result.Name);
    }

    [Fact]
    public async Task Validate_WrongKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        const string secret = "test-secret-at-least-32-chars-long!";
        var (_, plaintext) = await MakeSvc(db.CreateContext(), secret).CreateAsync("Real Key", []);

        // Flip one character in the body of the key (not the prefix so lookup still fires).
        var tampered = plaintext[..10] + (plaintext[10] == 'A' ? 'B' : 'A') + plaintext[11..];
        var result = await MakeSvc(db.CreateContext(), secret).ValidateAsync(tampered);

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_InactiveKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        const string secret = "test-secret-at-least-32-chars-long!";
        var (record, plaintext) = await MakeSvc(db.CreateContext(), secret).CreateAsync("Inactive Key", []);

        await using (var ctx = db.CreateContext())
        {
            var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
            stored!.IsActive = false;
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext(), secret).ValidateAsync(plaintext);
        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_ExpiredKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        const string secret = "test-secret-at-least-32-chars-long!";
        var (record, plaintext) = await MakeSvc(db.CreateContext(), secret).CreateAsync("Expired Key", []);

        await using (var ctx = db.CreateContext())
        {
            var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
            stored!.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext(), secret).ValidateAsync(plaintext);
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
        var (_, plaintext) = await MakeSvc(db.CreateContext(), "secret-one-xxxxxxxxxxxxxxxxxxxxxxxxxx").CreateAsync("Key A", []);

        var result = await MakeSvc(db.CreateContext(), "secret-two-xxxxxxxxxxxxxxxxxxxxxxxxxx").ValidateAsync(plaintext);
        Assert.Null(result);
    }

    // ── User-account association (C1 multi-tenant scoping prerequisite) ─────────

    [Fact]
    public async Task Create_WithUserAccountId_SetsOwner()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount("owner@example.com");
        await using (var seed = db.CreateContext())
        {
            seed.UserAccountEntities.Add(user);
            await seed.SaveChangesAsync();
        }

        var (record, _) = await MakeSvc(db.CreateContext())
            .CreateAsync("Owned Key", ["links:read"], user.Id);

        Assert.Equal(user.Id, record.UserAccountId);
    }

    [Fact]
    public async Task Create_WithUnknownUserAccountId_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var svc = MakeSvc(db.CreateContext());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("Orphan Key", [], Guid.CreateVersion7()));
    }

    // ── RevokeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_OwnActiveKey_DeactivatesAndBlocksAuth()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount("owner@example.com");
        await using (var seed = db.CreateContext())
        {
            seed.UserAccountEntities.Add(user);
            await seed.SaveChangesAsync();
        }

        var (record, plaintext) = await MakeSvc(db.CreateContext()).CreateAsync("k", ["links:read"], user.Id);

        var revoked = await MakeSvc(db.CreateContext()).RevokeAsync(record.Id, user.Id);
        Assert.True(revoked);

        await using var ctx = db.CreateContext();
        var stored = await ctx.ApiKeyEntities.FindAsync(record.Id);
        Assert.False(stored!.IsActive);

        // A revoked key can no longer authenticate.
        var validated = await MakeSvc(db.CreateContext()).ValidateAsync(plaintext);
        Assert.Null(validated);
    }

    [Fact]
    public async Task Revoke_AnotherUsersKey_ReturnsFalse_LeavesActive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var owner = EntityFactory.UserAccount("owner@example.com");
        var other = EntityFactory.UserAccount("other@example.com");
        await using (var seed = db.CreateContext())
        {
            seed.UserAccountEntities.AddRange(owner, other);
            await seed.SaveChangesAsync();
        }

        var (record, _) = await MakeSvc(db.CreateContext()).CreateAsync("k", [], owner.Id);

        var revoked = await MakeSvc(db.CreateContext()).RevokeAsync(record.Id, other.Id);
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
