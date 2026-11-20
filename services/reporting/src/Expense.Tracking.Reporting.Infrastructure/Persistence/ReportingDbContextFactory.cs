using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

/// <summary>Factory de design-time para 'dotnet ef' (nao conecta ao gerar migrations).</summary>
public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REPORTING_DB_CONNECTION")
            ?? "server=localhost;port=3306;database=expense_reporting;user=root;password=root;";

        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new ReportingDbContext(options);
    }
}
