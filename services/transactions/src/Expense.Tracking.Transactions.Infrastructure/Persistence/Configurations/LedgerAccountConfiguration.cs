using Expense.Tracking.Transactions.Domain.AccountingExport;
using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>CTB-5: plano de contas por usuario (categoria → conta contabil).</summary>
internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("ledger_accounts");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(l => l.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        // Binaria: "Software" e "software" sao categorias diferentes, como no Resumo (a collation
        // padrao do MySQL as juntaria no indice unico).
        builder.Property(l => l.Category).HasColumnName("category").HasMaxLength(Transaction.CategoryMaxLength).IsRequired()
            .UseCollation("utf8mb4_bin");
        builder.Property(l => l.Code).HasColumnName("code").HasMaxLength(LedgerAccount.CodeMaxLength).IsRequired();
        builder.Property(l => l.Name).HasColumnName("name").HasMaxLength(LedgerAccount.NameMaxLength);
        builder.Property(l => l.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(l => l.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(l => new { l.OwnerUserId, l.Category }).IsUnique();
    }
}
