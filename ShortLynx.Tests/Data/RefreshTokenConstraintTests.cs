using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;

namespace ShortLynx.Tests.Data;

public class RefreshTokenConstraintTests
{
    private static RefreshTokenEntity Token(Guid userId, string hash) => new()
    {
        Id = Guid.CreateVersion7(),
        UserAccountId = userId,
        TokenHash = hash,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
    };

    [Fact]
    public async Task TokenHash_MustBeUnique()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount();
        await using var ctx = db.CreateContext();
        ctx.UserAccountEntities.Add(user);
        ctx.RefreshTokenEntities.Add(Token(user.Id, "same-hash"));
        await ctx.SaveChangesAsync();

        ctx.RefreshTokenEntities.Add(Token(user.Id, "same-hash"));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingUser_CascadesRefreshTokens()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount();
        Guid tokenId;
        await using (var ctx = db.CreateContext())
        {
            ctx.UserAccountEntities.Add(user);
            var token = Token(user.Id, "h1");
            ctx.RefreshTokenEntities.Add(token);
            await ctx.SaveChangesAsync();
            tokenId = token.Id;
        }

        await using (var ctx = db.CreateContext())
        {
            ctx.UserAccountEntities.Remove(await ctx.UserAccountEntities.FindAsync(user.Id) ?? throw new());
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateContext())
            Assert.Null(await ctx.RefreshTokenEntities.FindAsync(tokenId));
    }
}
