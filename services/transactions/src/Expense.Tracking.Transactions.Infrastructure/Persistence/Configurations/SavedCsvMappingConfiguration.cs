using System.Text.Json;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Configurations;

/// <summary>
/// IMP-11: mapeamento de CSV salvo. Mapeamento e cabecalho vao em JSON: sao lidos e trocados
/// inteiros, nunca consultados por campo.
/// </summary>
internal sealed class SavedCsvMappingConfiguration : IEntityTypeConfiguration<SavedCsvMapping>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<SavedCsvMapping> builder)
    {
        builder.ToTable("csv_mappings");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(m => m.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(m => m.Name).HasColumnName("name").HasMaxLength(SavedCsvMapping.NameMaxLength).IsRequired();
        builder.Property(m => m.Mapping).HasColumnName("mapping").HasMaxLength(500).IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<CsvColumnMapping>(v, Json)!);
        builder.Property(m => m.Headers).HasColumnName("headers").HasColumnType("text").IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<string>>(v, Json)!,
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, h) => HashCode.Combine(hash, h.GetHashCode())),
                    v => v.ToList()));
        builder.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(m => m.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        // Nome unico por dono; a collation do MySQL ja ignora maiusculas, como SavedCsvMapping.HasName.
        builder.HasIndex(m => new { m.OwnerUserId, m.Name }).IsUnique();
    }
}
