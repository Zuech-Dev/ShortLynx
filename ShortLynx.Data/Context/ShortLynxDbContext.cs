using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;

namespace ShortLynx.Data.Context;

// IDataProtectionKeyContext: the DataProtection key ring is persisted in the database so every app
// (Core, Admin) shares ONE ring — required for tokens protected by one service to be readable by
// another, and it survives redeploys without a mounted volume.
public partial class ShortLynxDbContext(DbContextOptions<ShortLynxDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    public DbSet<AccountEntity> AccountEntities { get; set; }
    public DbSet<ApiKeyEntity> ApiKeyEntities { get; set; }
    public DbSet<CampaignEntity> CampaignEntities { get; set; }
    public DbSet<CustomDomainEntity> CustomDomainEntities { get; set; }
    public DbSet<LinkEntity> LinkEntities { get; set; }
    public DbSet<MembershipEntity> MembershipEntities { get; set; }
    public DbSet<RefreshTokenEntity> RefreshTokenEntities { get; set; }
    public DbSet<SocialConnectionEntity> SocialConnectionEntities { get; set; }
    public DbSet<SocialPostEntity> SocialPostEntities { get; set; }
    public DbSet<MagicLinkTokenEntity> MagicLinkTokenEntities { get; set; }
    public DbSet<ShortCodeEntity> ShortCodeEntities { get; set; }
    public DbSet<UserAccountEntity> UserAccountEntities { get; set; }
    public DbSet<UserLinkCodeEntity> UserLinkCodeEntities { get; set; }
    public DbSet<UserVisitEntity> UserVisitEntities { get; set; }
    public DbSet<VisitEntity> VisitEntities { get; set; }
}
