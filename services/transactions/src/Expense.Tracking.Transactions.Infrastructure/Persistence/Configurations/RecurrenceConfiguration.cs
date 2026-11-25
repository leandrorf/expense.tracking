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

        // RJT-2 (ADR-0010, decisao 1): vigencias depois da primeira (a primeira e a coluna amount).
        builder.Ignore(r => r.LaterAmounts);
        builder.Ignore(r => r.Amounts);
        builder.Ignore(r => r.FirstPeriod);
        builder.OwnsMany<RecurrenceAmount>("_laterAmounts", a =>
        {
            a.ToTable("recurrence_amounts");
            a.WithOwner().HasForeignKey("RecurrenceId");
            a.Property<Guid>("RecurrenceId").HasColumnName("recurrence_id");
            a.Property(x => x.FromPeriod).HasColumnName("from_period").HasMaxLength(7);
            a.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            a.HasKey("RecurrenceId", nameof(RecurrenceAmount.FromPeriod));
        });
        builder.Navigation("_laterAmounts").AutoInclude();

        // RJT-4 (ADR-0010, decisoes 3 e 4): reajuste anual na propria regra e os aniversarios aplicados.
        builder.Ignore(r => r.Adjustment);
        builder.Ignore(r => r.AppliedAdjustments);
        builder.Property<int?>("AdjustmentMonth").HasColumnName("adjustment_month");
        builder.Property<RecurrenceAdjustmentKind?>("AdjustmentKind").HasColumnName("adjustment_kind")
            .HasConversion<string>().HasMaxLength(16);
        builder.Property<EconomicIndex?>("AdjustmentIndex").HasColumnName("adjustment_index")
            .HasConversion<string>().HasMaxLength(8);
        builder.Property<decimal?>("AdjustmentRate").HasColumnName("adjustment_rate").HasPrecision(9, 4);
        builder.Property<bool>("AdjustmentNoReduction").HasColumnName("adjustment_no_reduction").IsRequired();
        builder.OwnsMany<RecurrenceAdjustmentApplied>("_appliedAdjustments", a =>
        {
            a.ToTable("recurrence_adjustments");
            a.WithOwner().HasForeignKey("RecurrenceId");
            a.Property<Guid>("RecurrenceId").HasColumnName("recurrence_id");
            a.Property(x => x.Year).HasColumnName("year").ValueGeneratedNever();
            a.Property(x => x.FromPeriod).HasColumnName("from_period").HasMaxLength(7).IsRequired();
            a.Property(x => x.PreviousAmount).HasColumnName("previous_amount").HasPrecision(18, 2).IsRequired();
            a.Property(x => x.NewAmount).HasColumnName("new_amount").HasPrecision(18, 2).IsRequired();
            a.Property(x => x.Rate).HasColumnName("rate").HasPrecision(9, 4);
            a.Property(x => x.AppliedAtUtc).HasColumnName("applied_at_utc").IsRequired();
            a.HasKey("RecurrenceId", nameof(RecurrenceAdjustmentApplied.Year));
        });
        builder.Navigation("_appliedAdjustments").AutoInclude();
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
