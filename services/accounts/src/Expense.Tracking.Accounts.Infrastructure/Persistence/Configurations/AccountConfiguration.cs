using Expense.Tracking.Accounts.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(Account.NameMaxLength).IsRequired();

        builder.Property(a => a.Scope)
            .HasColumnName("scope")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.TaxId).HasColumnName("tax_id").HasMaxLength(14);
        builder.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(a => a.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(a => a.OwnerUserId);
    }
}
