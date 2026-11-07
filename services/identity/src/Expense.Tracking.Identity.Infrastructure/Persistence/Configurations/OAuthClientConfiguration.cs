using Expense.Tracking.Identity.Domain.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    private static readonly ValueConverter<IReadOnlyCollection<string>, string> CollectionConverter =
        new(
            c => string.Join(' ', c),
            s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());

    private static readonly ValueComparer<IReadOnlyCollection<string>> CollectionComparer =
        new(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            c => c.Aggregate(0, (acc, v) => HashCode.Combine(acc, v.GetHashCode(StringComparison.Ordinal))),
            c => (IReadOnlyCollection<string>)c.ToList());

    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.ToTable("oauth_clients");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(c => c.ClientId).IsUnique();

        builder.Property(c => c.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.ClientSecretHash)
            .HasColumnName("client_secret_hash")
            .HasMaxLength(512);

        builder.Property(c => c.RequirePkce).HasColumnName("require_pkce").IsRequired();
        builder.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        ConfigureCollection(builder, c => c.RedirectUris, "redirect_uris");
        ConfigureCollection(builder, c => c.AllowedScopes, "allowed_scopes");
        ConfigureCollection(builder, c => c.AllowedGrantTypes, "allowed_grant_types");
    }

    private static void ConfigureCollection(
        EntityTypeBuilder<OAuthClient> builder,
        System.Linq.Expressions.Expression<Func<OAuthClient, IReadOnlyCollection<string>>> property,
        string columnName)
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasMaxLength(2000)
            .HasConversion(CollectionConverter, CollectionComparer)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();
    }
}
