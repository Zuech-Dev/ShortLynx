using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;

namespace ShortLynx.Data.Context;

public partial class ShortLynxDbContext() : DbContext
{
    public DbSet<ApiKeyEntity> ApiKeyEntities { get; set; }
    public DbSet<LinkEntity> LinkEntities { get; set; }
    public DbSet<ShortCodeEntity> ShortCodeEntities { get; set; }
    public DbSet<UserLinkCodeEntity> UserLinkCodeEntities { get; set; }
    public DbSet<UserVisitEntity> UserVisitEntities { get; set; }
    public DbSet<VisitEntity> VisitEntities { get; set; }
}
