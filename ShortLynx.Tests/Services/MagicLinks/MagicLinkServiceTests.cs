using Microsoft.Extensions.Options;
using ShortLynx.Services.MagicLinks;
using ShortLynx.Tests.Stubs;

namespace ShortLynx.Tests.Services.MagicLinks;

public class MagicLinkServiceTests
{
    private static MagicLinkService MakeSvc(
        ShortLynx.Data.Context.ShortLynxDbContext ctx,
        int expiryMinutes = 15,
        InMemoryEmailSender? emailSender = null)
        => new(ctx, emailSender ?? new InMemoryEmailSender(), Options.Create(new MagicLinkOptions
        {
            TokenExpiryMinutes = expiryMinutes,
            ConfirmationUrlBase = "https://example.com/auth/confirm",
        }));

    // ── CreateTokenAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_ReturnsNonEmptyToken()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("user@example.com");
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task CreateToken_CreatesUserAccount_WhenEmailIsNew()
    {
        await using var db = await TestDatabase.CreateAsync();
        await MakeSvc(db.CreateContext()).CreateTokenAsync("newuser@example.com");

        await using var ctx = db.CreateContext();
        var user = ctx.UserAccountEntities.SingleOrDefault(u => u.Email == "newuser@example.com");
        Assert.NotNull(user);
    }

    [Fact]
    public async Task CreateToken_ReusesExistingUser_WhenEmailAlreadyRegistered()
    {
        await using var db = await TestDatabase.CreateAsync();
        await MakeSvc(db.CreateContext()).CreateTokenAsync("repeat@example.com");
        await MakeSvc(db.CreateContext()).CreateTokenAsync("repeat@example.com");

        await using var ctx = db.CreateContext();
        var count = ctx.UserAccountEntities.Count(u => u.Email == "repeat@example.com");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateToken_NormalisesEmailToLowercase()
    {
        await using var db = await TestDatabase.CreateAsync();
        await MakeSvc(db.CreateContext()).CreateTokenAsync("  UPPER@EXAMPLE.COM  ");

        await using var ctx = db.CreateContext();
        var user = ctx.UserAccountEntities.SingleOrDefault(u => u.Email == "upper@example.com");
        Assert.NotNull(user);
    }

    [Fact]
    public async Task CreateToken_StoresHashedToken_NotPlaintext()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("hash@example.com");

        await using var ctx = db.CreateContext();
        var match = ctx.MagicLinkTokenEntities.Any(t => t.TokenHash == token);
        Assert.False(match);
    }

    [Fact]
    public async Task CreateToken_SetsExpiryFromOptions()
    {
        await using var db = await TestDatabase.CreateAsync();
        var before = DateTimeOffset.UtcNow;
        await MakeSvc(db.CreateContext(), expiryMinutes: 30).CreateTokenAsync("expiry@example.com");
        var after = DateTimeOffset.UtcNow;

        await using var ctx = db.CreateContext();
        var tokenEntity = ctx.MagicLinkTokenEntities
            .Single(t => t.UserAccount.Email == "expiry@example.com");

        Assert.True(tokenEntity.ExpiresAt >= before.AddMinutes(29));
        Assert.True(tokenEntity.ExpiresAt <= after.AddMinutes(31));
    }

    // ── ValidateTokenAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateToken_ValidToken_ReturnsUserAccount()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("valid@example.com");

        var user = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);

        Assert.NotNull(user);
        Assert.Equal("valid@example.com", user.Email);
    }

    [Fact]
    public async Task ValidateToken_UnknownToken_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var result = await MakeSvc(db.CreateContext()).ValidateTokenAsync("totally-fake-token-does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateToken_AlreadyUsedToken_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("used@example.com");

        // First validation succeeds.
        var first = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);
        Assert.NotNull(first);

        // Second validation must fail.
        var second = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);
        Assert.Null(second);
    }

    [Fact]
    public async Task ValidateToken_MarksTokenAsUsed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("mark@example.com");
        await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);

        await using var ctx = db.CreateContext();
        var tokenEntity = ctx.MagicLinkTokenEntities
            .Single(t => t.UserAccount.Email == "mark@example.com");
        Assert.NotNull(tokenEntity.UsedAt);
    }

    [Fact]
    public async Task ValidateToken_ExpiredToken_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("expired@example.com");

        await using (var ctx = db.CreateContext())
        {
            var tokenEntity = ctx.MagicLinkTokenEntities
                .Single(t => t.UserAccount.Email == "expired@example.com");
            tokenEntity.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        var result = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateToken_MultipleTokensForSameUser_ValidatesEachCorrectly()
    {
        await using var db = await TestDatabase.CreateAsync();
        var token1 = await MakeSvc(db.CreateContext()).CreateTokenAsync("multi@example.com");
        var token2 = await MakeSvc(db.CreateContext()).CreateTokenAsync("multi@example.com");

        // Validate the second token first.
        var user2 = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token2);
        Assert.NotNull(user2);

        // First token is still valid (unused).
        var user1 = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token1);
        Assert.NotNull(user1);
    }
}
