using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using Expense.Tracking.Transactions.Domain.Transactions;
using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

public sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<SavedCsvMapping> CsvMappings => Set<SavedCsvMapping>();

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
