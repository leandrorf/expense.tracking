using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>REC-2 (ADR-0008, decisao 2): regras de recorrencia.</summary>
internal sealed class RecurrenceConfiguration : IEntityTypeConfiguration<Recurrence>
{
    public void Configure(EntityTypeBuilder<Recurrence> builder)
    {
        builder.ToTable("recurrences");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(r => r.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(r => r.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.PaymentMethod).HasColumnName("payment_method").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.CreditCardId).HasColumnName("credit_card_id");
        builder.Property(r => r.Description).HasColumnName("description").HasMaxLength(Transaction.DescriptionMaxLength).IsRequired();
        builder.Property(r => r.Category).HasColumnName("category").HasMaxLength(Transaction.CategoryMaxLength).IsRequired();
        builder.Property(r => r.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(r => r.Notes).HasColumnName("notes").HasMaxLength(Transaction.NotesMaxLength);
        builder.Property(r => r.Frequency).HasColumnName("frequency").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.StartsOn).HasColumnName("starts_on").IsRequired();
        builder.Property(r => r.EndsOn).HasColumnName("ends_on");
        builder.Property(r => r.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(r => r.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(r => r.OwnerUserId);
    }
}

/// <summary>REC-3 (ADR-0008, decisao 4): periodos fechados, um por (regra, periodo).</summary>
internal sealed class RecurrenceOccurrenceConfiguration : IEntityTypeConfiguration<RecurrenceOccurrence>
{
    public void Configure(EntityTypeBuilder<RecurrenceOccurrence> builder)
    {
        builder.ToTable("recurrence_occurrences");

        builder.HasKey(o => new { o.RecurrenceId, o.Period });
        builder.Property(o => o.RecurrenceId).HasColumnName("recurrence_id").IsRequired();
        builder.Property(o => o.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
        builder.Property(o => o.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(o => o.TransactionId).HasColumnName("transaction_id");
        builder.Property(o => o.ClosedAtUtc).HasColumnName("closed_at_utc").IsRequired();

        builder.HasIndex(o => o.TransactionId);
        builder.HasIndex(o => o.OwnerUserId);
    }
}
