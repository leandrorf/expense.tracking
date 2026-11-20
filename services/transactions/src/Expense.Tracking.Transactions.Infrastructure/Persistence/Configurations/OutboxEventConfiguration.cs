using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");

        builder.HasKey(e => e.Sequence);
        builder.Property(e => e.Sequence).HasColumnName("sequence").ValueGeneratedOnAdd();

        builder.Property(e => e.EventId).HasColumnName("event_id").IsRequired();
        builder.HasIndex(e => e.EventId).IsUnique().HasDatabaseName("ux_outbox_events_event_id");

        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();

        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired();
        // Usado pela carga inicial ("este lancamento ja tem evento?").
        builder.HasIndex(e => new { e.AggregateId, e.EventType }).HasDatabaseName("ix_outbox_events_aggregate");

        builder.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(e => e.TraceParent).HasColumnName("trace_parent").HasMaxLength(64);
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("longtext").IsRequired();
    }
}
