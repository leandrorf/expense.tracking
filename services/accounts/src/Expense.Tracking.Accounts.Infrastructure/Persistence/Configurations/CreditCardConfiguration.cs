using Expense.Tracking.Accounts.Domain.Cards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence.Configurations;

internal sealed class CreditCardConfiguration : IEntityTypeConfiguration<CreditCard>
{
    public void Configure(EntityTypeBuilder<CreditCard> builder)
    {
        builder.ToTable("credit_cards");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(c => c.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(c => c.Label).HasColumnName("label").HasMaxLength(CreditCard.LabelMaxLength).IsRequired();
        builder.Property(c => c.Brand).HasColumnName("brand").HasMaxLength(40).IsRequired();
        builder.Property(c => c.LastFourDigits).HasColumnName("last_four_digits").HasMaxLength(4).IsRequired();
        builder.Property(c => c.CreditLimit).HasColumnName("credit_limit").HasPrecision(18, 2).IsRequired();
        builder.Property(c => c.ClosingDay).HasColumnName("closing_day").IsRequired();
        builder.Property(c => c.DueDay).HasColumnName("due_day").IsRequired();
        builder.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(c => c.OwnerUserId);
        builder.HasIndex(c => c.AccountId);
    }
}
