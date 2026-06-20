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
                    // Nullable ApiKeyId makes this relationship optional (API-key-owned links).
                    entity.HasOne<ApiKeyEntity>(e => e.ApiKey).WithMany(l => l.Links);
                    // User-owned links (dashboard-created). No cascade — deleting a user is handled
                    // in application logic, consistent with the ApiKey → UserAccount relationship.
                    entity.HasOne(e => e.UserAccount)
                          .WithMany()
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.NoAction);
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
                    entity.HasOne<UserAccountEntity>(e => e.UserAccount)
                          .WithMany()
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.NoAction);
                }
            )
           .Entity<UserAccountEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => e.Email).IsUnique();
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Email).IsRequired().HasMaxLength(254);
                }
            )
           .Entity<MagicLinkTokenEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => e.TokenHash);
                    entity.HasOne<UserAccountEntity>(e => e.UserAccount)
                          .WithMany(u => u.MagicLinkTokens)
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                }
            )
           .Entity<CustomDomainEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasIndex(e => e.Domain).IsUnique();
                    entity.HasOne<UserAccountEntity>(e => e.UserAccount)
                          .WithMany(u => u.CustomDomains)
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.VerificationStatus).HasConversion<int>();
                }
            );

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
