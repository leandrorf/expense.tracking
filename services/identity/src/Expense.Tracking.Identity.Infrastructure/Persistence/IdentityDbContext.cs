using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Infrastructure.Persistence;

/// <summary>Contexto EF Core do servico de identidade.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<OAuthClient> Clients => Set<OAuthClient>();

    public DbSet<AuthorizationCode> AuthorizationCodes => Set<AuthorizationCode>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // MySQL nao possui tipo nativo para DateTimeOffset: persistimos como DateTime em UTC.
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
