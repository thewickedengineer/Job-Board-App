using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentBridge.Post.Domain.Managers;

namespace TalentBridge.Post.Infrastructure.Persistence.Configurations;

internal sealed class ManagerConfiguration : IEntityTypeConfiguration<Manager>
{
    public void Configure(EntityTypeBuilder<Manager> b)
    {
        b.ToTable("managers");
        b.HasKey(m => m.Id);

        b.Property(m => m.Email).HasColumnType("citext").HasMaxLength(320).IsRequired();
        b.HasIndex(m => m.Email).IsUnique();

        b.Property(m => m.PasswordHash).IsRequired();
        b.Property(m => m.FullName).HasMaxLength(120).IsRequired();
        b.Property(m => m.Organization).HasMaxLength(120).IsRequired();
        b.Property(m => m.EmailVerified).HasDefaultValue(false);
        b.Property(m => m.CreatedAt).IsRequired();
        b.Property(m => m.LastLoginAt);

        b.HasMany(m => m.RefreshTokens)
            .WithOne()
            .HasForeignKey(t => t.ManagerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(m => m.RefreshTokens).HasField("_refreshTokens");
    }
}
