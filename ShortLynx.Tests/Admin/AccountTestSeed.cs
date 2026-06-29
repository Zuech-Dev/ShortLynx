using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Admin;

internal static class AccountTestSeed
{
    /// <summary>
    /// Seeds a user, an account, and an Owner membership so the dashboard's AccountResolver returns a
    /// known account id for <paramref name="userId"/>. Returns that account id.
    /// </summary>
    public static Guid SeedOwner(ShortLynxDbContext db, Guid userId, string email = "user@example.com")
    {
        var account = new AccountEntity
        {
            Id = Guid.CreateVersion7(), Name = email, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        db.Add(new UserAccountEntity
        {
            Id = userId, Email = email, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        });
        db.Add(account);
        db.Add(new MembershipEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, UserAccountId = userId,
            Role = AccountRole.Owner, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return account.Id;
    }
}
