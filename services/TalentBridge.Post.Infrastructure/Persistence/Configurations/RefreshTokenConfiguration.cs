using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentBridge.Post.Domain.Managers;

namespace TalentBridge.Post.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();

        b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.ManagerId);

        b.Property(t => t.ExpiresAt).IsRequired();
        b.Property(t => t.RevokedAt);
        b.Property(t => t.CreatedAt).IsRequired();
    }
}
