using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Context;

public partial class ShortLynxDbContext
{
    /*
     * Concentions:
     * - FK Owner defines the relationship from one end
     * - Only define properties overriding EF core's conventions or it cannot be inferred
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
           .Entity<LinkEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasOne<ApiKeyEntity>(e => e.ApiKey).WithMany(l => l.Links);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Mode).HasConversion<int>();
                    entity.Property(e => e.OriginalUrl).IsRequired();
                }
            )
           .Entity<ApiKeyEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Prefix).HasMaxLength(8);
                }
            );

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
