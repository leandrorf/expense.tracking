using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

public sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransactionsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
