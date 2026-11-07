using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();

        builder.Property(t => t.Type)
            .HasColumnName("type").HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(t => t.PaymentMethod)
            .HasColumnName("payment_method").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(t => t.CreditCardId).HasColumnName("credit_card_id");
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(Transaction.DescriptionMaxLength).IsRequired();
        builder.Property(t => t.Category).HasColumnName("category").HasMaxLength(Transaction.CategoryMaxLength).IsRequired();
        builder.Property(t => t.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(t => t.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(t => t.Notes).HasColumnName("notes").HasMaxLength(Transaction.NotesMaxLength);
        builder.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(t => t.OwnerUserId);
        builder.HasIndex(t => new { t.OwnerUserId, t.AccountId });
        builder.HasIndex(t => new { t.OwnerUserId, t.OccurredOn });
    }
}
