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
           .Entity<UserLinkCodeEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => new { e.LinkId, e.UserId }).IsUnique();
                    entity.HasIndex(e => e.Code).IsUnique();
                    entity.HasOne<LinkEntity>(e => e.Link).WithMany();
                    entity.Property(e => e.Id).ValueGeneratedNever();
                }
            )
           .Entity<ShortCodeEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => e.LinkId).IsUnique();
                    entity.HasIndex(e => e.Code).IsUnique();
                    entity.HasOne<LinkEntity>(e => e.Link).WithOne();
                    entity.Property(e => e.Id).ValueGeneratedNever();
                }
            )
           .Entity<VisitEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasOne<ShortCodeEntity>(e => e.ShortCode).WithMany();
                    entity.Property(e => e.Id).ValueGeneratedNever();
                }
            )
           .Entity<UserVisitEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasOne<UserLinkCodeEntity>(e => e.UserLinkCode).WithMany();
                    // TODO: UserId denormalized from UserLinkCode
                    entity.Property(e => e.Id).ValueGeneratedNever();
                }
            )
           .Entity<ApiKeyEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => e.Prefix);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Prefix).HasMaxLength(8);
                }
            );

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
