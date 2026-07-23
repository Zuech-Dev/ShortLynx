using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Services.MagicLinks;
using ShortLynx.Tests.Stubs;

namespace ShortLynx.Tests.Services.MagicLinks;

public class MagicLinkServiceTests
{
    private static MagicLinkService MakeSvc(
        ShortLynx.Data.Context.ShortLynxDbContext ctx,
        int expiryMinutes = 15,
        InMemoryEmailSender? emailSender = null,
        string[]? allowlist = null)
        => new(ctx, emailSender ?? new InMemoryEmailSender(),
            Options.Create(new MagicLinkOptions
            {
                TokenExpiryMinutes = expiryMinutes,
                ConfirmationUrlBase = "https://example.com/auth/confirm",
            }),
            Options.Create(new ShortLynx.Services.Auth.AccessControlOptions { AllowedEmails = allowlist ?? [] }));

    private static async Task SeedUserAsync(
        ShortLynx.Data.Context.ShortLynxDbContext ctx, string email, bool isActive = true)
    {
        ctx.UserAccountEntities.Add(new ShortLynx.Data.Entities.UserAccountEntity
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim().ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = isActive,
        });
        await ctx.SaveChangesAsync();
    }

    // ── CreateTokenAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_AllowlistedEmail_NoExistingUser_ProvisionsAndReturnsToken()
    {
        await using var db = await TestDatabase.CreateAsync();

        var token = await MakeSvc(db.CreateContext(), allowlist: ["boot@example.com"])
            .CreateTokenAsync("boot@example.com");

        Assert.False(string.IsNullOrEmpty(token)); // first-admin bootstrap must work on a fresh install

        await using var check = db.CreateContext();
        var user = await check.UserAccountEntities.SingleAsync(u => u.Email == "boot@example.com");
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task CreateToken_UnknownNonAllowlistedEmail_ReturnsEmpty_AndCreatesNoUser()
    {
        await using var db = await TestDatabase.CreateAsync();

        var token = await MakeSvc(db.CreateContext(), allowlist: [])
            .CreateTokenAsync("stranger@example.com");

        Assert.Equal(string.Empty, token); // no anonymous provisioning / email-bombing
        await using var check = db.CreateContext();
        Assert.False(await check.UserAccountEntities.AnyAsync(u => u.Email == "stranger@example.com"));
    }

    [Fact]
    public async Task CreateToken_DeactivatedUser_ReturnsEmpty_EvenIfAllowlisted()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "gone@example.com", isActive: false);

        var token = await MakeSvc(db.CreateContext(), allowlist: ["gone@example.com"])
            .CreateTokenAsync("gone@example.com");

        Assert.Equal(string.Empty, token); // deactivation wins over the allowlist
    }

    [Fact]
    public async Task CreateToken_ReturnsNonEmptyToken_ForActiveUser()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "user@example.com");

        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("user@example.com");
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task CreateToken_DoesNotCreateUserAccount_WhenEmailIsUnknown()
    {
        await using var db = await TestDatabase.CreateAsync();
        var email = new InMemoryEmailSender();
        var token = await MakeSvc(db.CreateContext(), emailSender: email)
            .CreateTokenAsync("newuser@example.com");

        Assert.Empty(token);
        Assert.Empty(email.Sent);

        await using var ctx = db.CreateContext();
        var user = ctx.UserAccountEntities.SingleOrDefault(u => u.Email == "newuser@example.com");
        Assert.Null(user);
        Assert.Empty(ctx.MagicLinkTokenEntities);
    }

    [Fact]
    public async Task CreateToken_DoesNotSendOrCreate_WhenUserIsInactive()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "inactive@example.com", isActive: false);

        var email = new InMemoryEmailSender();
        var token = await MakeSvc(db.CreateContext(), emailSender: email)
            .CreateTokenAsync("inactive@example.com");

        Assert.Empty(token);
        Assert.Empty(email.Sent);

        await using var ctx = db.CreateContext();
        Assert.Empty(ctx.MagicLinkTokenEntities);
    }

    [Fact]
    public async Task CreateToken_ReusesExistingUser_WhenEmailAlreadyRegistered()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "repeat@example.com");

        await MakeSvc(db.CreateContext()).CreateTokenAsync("repeat@example.com");
        await MakeSvc(db.CreateContext()).CreateTokenAsync("repeat@example.com");

        await using var ctx = db.CreateContext();
        var count = ctx.UserAccountEntities.Count(u => u.Email == "repeat@example.com");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateToken_NormalisesEmailToLowercase_ForLookup()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "upper@example.com");

        var email = new InMemoryEmailSender();
        var token = await MakeSvc(db.CreateContext(), emailSender: email)
            .CreateTokenAsync("  UPPER@EXAMPLE.COM  ");

        Assert.NotEmpty(token);
        Assert.Contains(email.Sent, e => e.To == "upper@example.com");
    }

    [Fact]
    public async Task CreateToken_StoresHashedToken_NotPlaintext()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "hash@example.com");

        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("hash@example.com");

        await using var ctx = db.CreateContext();
        var match = ctx.MagicLinkTokenEntities.Any(t => t.TokenHash == token);
        Assert.False(match);
    }

    [Fact]
    public async Task CreateToken_SetsExpiryFromOptions()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "expiry@example.com");

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
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "valid@example.com");

        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("valid@example.com");

        var user = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);

        Assert.NotNull(user);
        Assert.Equal("valid@example.com", user.Email);
    }

    [Fact]
    public async Task ValidateToken_InactiveUser_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "inactive@example.com");

        var token = await MakeSvc(db.CreateContext()).CreateTokenAsync("inactive@example.com");

        // Deactivate the user (soft delete) after the token was issued.
        await using (var ctx = db.CreateContext())
        {
            await ctx.UserAccountEntities
                .Where(u => u.Email == "inactive@example.com")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        var result = await MakeSvc(db.CreateContext()).ValidateTokenAsync(token);
        Assert.Null(result);
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
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "used@example.com");

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
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "mark@example.com");

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
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "expired@example.com");

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
        await using (var seed = db.CreateContext())
            await SeedUserAsync(seed, "multi@example.com");

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
