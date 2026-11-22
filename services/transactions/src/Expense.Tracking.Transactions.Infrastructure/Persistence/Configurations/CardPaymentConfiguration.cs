using Expense.Tracking.Transactions.Domain.CardPayments;
using Expense.Tracking.Transactions.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>INV-6 (ADR-0005, decisao 6): pagamentos de fatura, separados dos lancamentos.</summary>
internal sealed class CardPaymentConfiguration : IEntityTypeConfiguration<CardPayment>
{
    public void Configure(EntityTypeBuilder<CardPayment> builder)
    {
        builder.ToTable("card_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(p => p.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(p => p.CreditCardId).HasColumnName("credit_card_id").IsRequired();
        builder.Property(p => p.InvoiceDueMonth).HasColumnName("invoice_due_month").IsRequired();
        builder.Property(p => p.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(p => p.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.PaidOn).HasColumnName("paid_on").IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(p => p.ExternalId).HasColumnName("external_id").HasMaxLength(ImportLine.ExternalIdMaxLength);
        builder.Property(p => p.ImportBatchId).HasColumnName("import_batch_id");

        builder.HasIndex(p => new { p.OwnerUserId, p.CreditCardId, p.InvoiceDueMonth });
        // INV-11: a mesma linha do extrato nao vira dois pagamentos (duas confirmacoes em paralelo).
        builder.HasIndex(p => new { p.OwnerUserId, p.AccountId, p.ExternalId }).IsUnique();
        builder.HasIndex(p => p.ImportBatchId);
    }
}
