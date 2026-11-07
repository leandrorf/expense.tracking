using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expense.Tracking.Identity.Infrastructure.Persistence;

/// <summary>
/// Factory usada pelas ferramentas do EF Core (dotnet ef) em tempo de design.
/// Nao conecta ao banco ao gerar migrations; a connection string e apenas para parsing.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION")
            ?? "server=localhost;port=3306;database=expense_identity;user=root;password=root;";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new IdentityDbContext(options);
    }
}
