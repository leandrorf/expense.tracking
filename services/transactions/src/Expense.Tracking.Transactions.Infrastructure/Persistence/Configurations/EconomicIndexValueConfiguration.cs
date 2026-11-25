using Expense.Tracking.Transactions.Domain.Recurrences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>RJT-6 (ADR-0010, decisao 6): variacoes mensais dos indices oficiais, uma por (indice, mes).</summary>
internal sealed class EconomicIndexValueConfiguration : IEntityTypeConfiguration<EconomicIndexValue>
{
    public void Configure(EntityTypeBuilder<EconomicIndexValue> builder)
    {
        builder.ToTable("economic_indexes");

        builder.HasKey(i => new { i.Index, i.Month });
        builder.Property(i => i.Index).HasColumnName("index_code").HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(i => i.Month).HasColumnName("month").HasMaxLength(7).IsRequired();
        builder.Property(i => i.Rate).HasColumnName("rate").HasPrecision(9, 4).IsRequired();
        builder.Property(i => i.FetchedAtUtc).HasColumnName("fetched_at_utc").IsRequired();
    }
}
