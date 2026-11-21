using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>IMP-4: lote de importacao pendente/confirmado/descartado (ADR-0004, decisao 2).</summary>
internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("import_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(b => b.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(b => b.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(b => b.CreditCardId).HasColumnName("credit_card_id");
        builder.Property(b => b.Format).HasColumnName("format").HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(b => b.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(b => b.DefaultPaymentMethod)
            .HasColumnName("default_payment_method").HasConversion<string>().HasMaxLength(20).IsRequired();
        // IMP-5: a confirmacao grava "WHERE status = 'Pending'"; duas confirmacoes em paralelo nao passam.
        builder.Property(b => b.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16)
            .IsRequired().IsConcurrencyToken();
        builder.Property(b => b.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(b => b.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(b => b.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");

        builder.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(b => b.OwnerUserId);
        builder.HasIndex(b => new { b.Status, b.ExpiresAtUtc });
    }
}

internal sealed class ImportLineConfiguration : IEntityTypeConfiguration<ImportLine>
{
    public void Configure(EntityTypeBuilder<ImportLine> builder)
    {
        builder.ToTable("import_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(l => l.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(l => l.Number).HasColumnName("number").IsRequired();
        builder.Property(l => l.OccurredOn).HasColumnName("occurred_on");
        builder.Property(l => l.Amount).HasColumnName("amount").HasPrecision(18, 2);
        builder.Property(l => l.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.Description).HasColumnName("description").HasMaxLength(Transaction.DescriptionMaxLength).IsRequired();
        builder.Property(l => l.ExternalId).HasColumnName("external_id").HasMaxLength(ImportLine.ExternalIdMaxLength);
        builder.Property(l => l.PaymentMethod).HasColumnName("payment_method").HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Category).HasColumnName("category").HasMaxLength(Transaction.CategoryMaxLength);
        builder.Property(l => l.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(l => l.Included).HasColumnName("included").IsRequired();
        builder.Property(l => l.Problem).HasColumnName("problem").HasMaxLength(200);
        builder.Property(l => l.TransactionId).HasColumnName("transaction_id");

        builder.HasIndex(l => new { l.BatchId, l.Number }).IsUnique();
    }
}
