using Expense.Tracking.Reporting.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Reporting.IntegrationTests;

public sealed class ReportingApiFactory : WebApplicationFactory<Program>
{
    public FakeTransactionsClient Transactions { get; } = new();

    public FakeAccountsClient Accounts { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Authority"] = "http://localhost",
                ["Services:TransactionsBaseUrl"] = "http://localhost:5100",
                ["Services:AccountsBaseUrl"] = "http://localhost:5090",
            });
        });

        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d => d.ServiceType == typeof(ITransactionsClient) || d.ServiceType == typeof(IAccountsClient))
                .ToList();
            foreach (var d in toRemove)
            {
                services.Remove(d);
            }

            services.AddSingleton<ITransactionsClient>(Transactions);
            services.AddSingleton<IAccountsClient>(Accounts);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
