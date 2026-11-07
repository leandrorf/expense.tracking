using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence;

/// <summary>Factory de design-time para 'dotnet ef' (nao conecta ao gerar migrations).</summary>
public sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ACCOUNTS_DB_CONNECTION")
            ?? "server=localhost;port=3306;database=expense_accounts;user=root;password=root;";

        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new AccountsDbContext(options);
    }
}
