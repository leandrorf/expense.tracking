using System.Text.Json;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>
/// CTB-6: registro da ultima exportacao de cada mes e conta. Os totais vao em JSON: sao lidos e
/// trocados inteiros, nunca consultados por campo. Nada do conteudo do arquivo e guardado.
/// </summary>
internal sealed class AccountingExportRecordConfiguration : IEntityTypeConfiguration<AccountingExportRecord>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AccountingExportRecord> builder)
    {
        builder.ToTable("accounting_exports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(r => r.Month).HasColumnName("month").IsRequired();
        builder.Property(r => r.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(r => r.Lines).HasColumnName("lines").IsRequired();
        builder.Property(r => r.Payments).HasColumnName("payments").IsRequired();
        builder.Property(r => r.Totals).HasColumnName("totals").HasColumnType("text").IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<AccountingExportTotal>>(v, Json)!,
                new ValueComparer<IReadOnlyList<AccountingExportTotal>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, t) => HashCode.Combine(hash, t.GetHashCode())),
                    v => v.ToList()));
        builder.Property(r => r.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(r => r.ExportedAtUtc).HasColumnName("exported_at_utc").IsRequired();

        builder.HasIndex(r => new { r.OwnerUserId, r.Month, r.AccountId }).IsUnique();
    }
}
