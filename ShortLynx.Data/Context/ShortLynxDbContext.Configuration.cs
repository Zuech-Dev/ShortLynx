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
                    // Optional pin to a verified custom domain. Removing the domain unpins the link
                    // (SetNull) rather than blocking the delete.
                    entity.HasOne(e => e.CustomDomain)
                          .WithMany()
                          .HasForeignKey(e => e.CustomDomainId)
                          .OnDelete(DeleteBehavior.SetNull);
                    // Optional campaign grouping. Deleting a campaign unassigns its links (SetNull)
                    // rather than cascading the delete to the links themselves.
                    entity.HasOne(e => e.Campaign)
                          .WithMany(c => c.Links)
                          .HasForeignKey(e => e.CampaignId)
                          .OnDelete(DeleteBehavior.SetNull);
                    // Owning account — deleting an account removes its links.
                    entity.HasOne(e => e.Account)
                          .WithMany()
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
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
                    entity.Property(e => e.Source).HasConversion<int>();
                    entity.Property(e => e.Device).HasConversion<int>();
                }
            )
           .Entity<UserVisitEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.HasOne<UserLinkCodeEntity>(e => e.UserLinkCode).WithMany();
                    // TODO: UserId denormalized from UserLinkCode
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Source).HasConversion<int>();
                    entity.Property(e => e.Device).HasConversion<int>();
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
                    // Owning account — deleting an account removes its keys.
                    entity.HasOne(e => e.Account)
                          .WithMany()
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
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
                    // UserAccountId is now audit-only (who added it); ownership is the account.
                    entity.HasOne(e => e.UserAccount)
                          .WithMany(u => u.CustomDomains)
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.NoAction);
                    // Owning account — deleting an account removes its domains.
                    entity.HasOne(e => e.Account)
                          .WithMany()
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.VerificationStatus).HasConversion<int>();
                }
            )
           .Entity<AccountEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                }
            )
           .Entity<CampaignEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                    // Owning account — deleting an account removes its campaigns.
                    entity.HasOne(e => e.Account)
                          .WithMany()
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    // Audit-only creator; deleting the user doesn't touch the campaign.
                    entity.HasOne(e => e.UserAccount)
                          .WithMany()
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.NoAction);
                }
            )
           .Entity<MembershipEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    // A user has at most one membership per account.
                    entity.HasIndex(e => new { e.AccountId, e.UserAccountId }).IsUnique();
                    entity.Property(e => e.Role).HasConversion<int>();
                    entity.HasOne(e => e.Account)
                          .WithMany(a => a.Memberships)
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    entity.HasOne(e => e.UserAccount)
                          .WithMany(u => u.Memberships)
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                }
            )
           .Entity<RefreshTokenEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.HasIndex(e => e.TokenHash).IsUnique();
                    entity.HasOne(e => e.UserAccount)
                          .WithMany()
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                }
            )
           .Entity<SocialPostEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Platform).HasConversion<int>();
                    entity.HasIndex(e => e.LinkId);
                    entity.HasIndex(e => e.AccountId);
                    // Owning account — deleting an account removes its posts.
                    entity.HasOne(e => e.Account)
                          .WithMany()
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    // Deleting a link removes its posting history with it.
                    entity.HasOne(e => e.Link)
                          .WithMany()
                          .HasForeignKey(e => e.LinkId)
                          .OnDelete(DeleteBehavior.Cascade);
                    // Disconnecting the social account keeps the post record (SetNull), platform +
                    // handle stay readable because they're denormalized onto the post.
                    entity.HasOne(e => e.SocialConnection)
                          .WithMany()
                          .HasForeignKey(e => e.SocialConnectionId)
                          .OnDelete(DeleteBehavior.SetNull);
                }
            )
           .Entity<SocialConnectionEntity>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Id).ValueGeneratedNever();
                    entity.Property(e => e.Platform).HasConversion<int>();
                    // One connection per (account, platform, external account).
                    entity.HasIndex(e => new { e.AccountId, e.Platform, e.ExternalAccountId }).IsUnique();
                    // Owning account — deleting an account removes its connections.
                    entity.HasOne(e => e.Account)
                          .WithMany()
                          .HasForeignKey(e => e.AccountId)
                          .OnDelete(DeleteBehavior.Cascade);
                    // Audit-only creator; deleting the user doesn't touch the connection.
                    entity.HasOne(e => e.UserAccount)
                          .WithMany()
                          .HasForeignKey(e => e.UserAccountId)
                          .OnDelete(DeleteBehavior.NoAction);
                }
            );

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
