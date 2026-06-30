using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;

namespace ShortLynx.Data.Context;

public partial class ShortLynxDbContext(DbContextOptions<ShortLynxDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> AccountEntities { get; set; }
    public DbSet<ApiKeyEntity> ApiKeyEntities { get; set; }
    public DbSet<CampaignEntity> CampaignEntities { get; set; }
    public DbSet<CustomDomainEntity> CustomDomainEntities { get; set; }
    public DbSet<LinkEntity> LinkEntities { get; set; }
    public DbSet<MembershipEntity> MembershipEntities { get; set; }
    public DbSet<RefreshTokenEntity> RefreshTokenEntities { get; set; }
    public DbSet<MagicLinkTokenEntity> MagicLinkTokenEntities { get; set; }
    public DbSet<ShortCodeEntity> ShortCodeEntities { get; set; }
    public DbSet<UserAccountEntity> UserAccountEntities { get; set; }
    public DbSet<UserLinkCodeEntity> UserLinkCodeEntities { get; set; }
    public DbSet<UserVisitEntity> UserVisitEntities { get; set; }
    public DbSet<VisitEntity> VisitEntities { get; set; }
}
