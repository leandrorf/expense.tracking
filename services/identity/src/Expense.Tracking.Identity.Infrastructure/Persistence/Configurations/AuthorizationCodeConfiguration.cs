using Expense.Tracking.Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Configurations;

internal sealed class AuthorizationCodeConfiguration : IEntityTypeConfiguration<AuthorizationCode>
{
    public void Configure(EntityTypeBuilder<AuthorizationCode> builder)
    {
        builder.ToTable("authorization_codes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(c => c.CodeHash).IsUnique();

        builder.Property(c => c.ClientId).HasColumnName("client_id").HasMaxLength(128).IsRequired();
        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(c => c.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(2000).IsRequired();
        builder.Property(c => c.Scopes).HasColumnName("scopes").HasMaxLength(2000).IsRequired();
        builder.Property(c => c.CodeChallenge).HasColumnName("code_challenge").HasMaxLength(256).IsRequired();
        builder.Property(c => c.CodeChallengeMethod).HasColumnName("code_challenge_method").HasMaxLength(16).IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(c => c.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(c => c.ConsumedAtUtc).HasColumnName("consumed_at_utc");
    }
}
