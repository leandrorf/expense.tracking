using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Transactions.IntegrationTests;

public sealed class TransactionsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"transactions-tests-{Guid.NewGuid():N}";

    public FakeAccountsClient AccountsClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TransactionsDb"] = "server=localhost;database=ignored;user=root;password=root;",
                ["Jwt:Authority"] = "http://localhost",
                ["Services:AccountsBaseUrl"] = "http://localhost:5090",
            });
        });

        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<TransactionsDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(TransactionsDbContext) ||
                    d.ServiceType == typeof(IAccountsClient) ||
                    (d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true
                        && d.ServiceType.GenericTypeArguments.Contains(typeof(TransactionsDbContext))))
                .ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TransactionsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddSingleton<IAccountsClient>(AccountsClient);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
