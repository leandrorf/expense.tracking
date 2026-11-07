using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

/// <summary>Factory de design-time para 'dotnet ef' (nao conecta ao gerar migrations).</summary>
public sealed class TransactionsDbContextFactory : IDesignTimeDbContextFactory<TransactionsDbContext>
{
    public TransactionsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRANSACTIONS_DB_CONNECTION")
            ?? "server=localhost;port=3306;database=expense_transactions;user=root;password=root;";

        var options = new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new TransactionsDbContext(options);
    }
}
