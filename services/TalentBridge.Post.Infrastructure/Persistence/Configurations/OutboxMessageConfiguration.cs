using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentBridge.Post.Domain.Outbox;

namespace TalentBridge.Post.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).UseSerialColumn();

        b.Property(m => m.AggregateId).IsRequired();
        b.Property(m => m.Type).HasMaxLength(100).IsRequired();
        b.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(m => m.OccurredAt).IsRequired();
        b.Property(m => m.ProcessedAt);
        b.Property(m => m.Attempts).IsRequired().HasDefaultValue(0);
        b.Property(m => m.LastError);

        // The publisher polls "unprocessed, oldest first". A partial index keeps
        // that lookup cheap no matter how large the processed history grows.
        b.HasIndex(m => m.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at is null");
    }
}
