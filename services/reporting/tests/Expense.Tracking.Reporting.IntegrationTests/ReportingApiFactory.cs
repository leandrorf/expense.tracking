using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// Factory dos testes: clients HTTP falsos, banco da projecao em memoria e o feed de eventos falso.
/// O worker da projecao fica desligado por padrao (os testes chamam o processador); subclasses
/// mudam configuracoes por <see cref="ConfigureTestSettings"/>.
/// </summary>
public class ReportingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"reporting-tests-{Guid.NewGuid():N}";

    public FakeTransactionsClient Transactions { get; } = new();

    public FakeAccountsClient Accounts { get; } = new();

    public FakeEventFeed EventFeed { get; } = new();

    protected virtual void ConfigureTestSettings(IDictionary<string, string?> settings)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Authority"] = "http://localhost",
            ["Services:TransactionsBaseUrl"] = "http://localhost:5100",
            ["Services:AccountsBaseUrl"] = "http://localhost:5090",
            ["ConnectionStrings:ReportingDb"] = "server=localhost;database=ignored;user=root;password=root;",
            ["Projection:Enabled"] = "false",
        };
        ConfigureTestSettings(settings);

        // UseSetting: com minimal hosting, so assim o Program ja ve os valores ao registrar servicos.
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(ITransactionsClient) ||
                    d.ServiceType == typeof(IAccountsClient) ||
                    d.ServiceType == typeof(IEventFeedClient) ||
                    d.ServiceType == typeof(DbContextOptions<ReportingDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ReportingDbContext) ||
                    (d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true
                        && d.ServiceType.GenericTypeArguments.Contains(typeof(ReportingDbContext))))
                .ToList();
            foreach (var d in toRemove)
            {
                services.Remove(d);
            }

            services.AddDbContext<ReportingDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddSingleton<ITransactionsClient>(Transactions);
            services.AddSingleton<IAccountsClient>(Accounts);
            services.AddSingleton<IEventFeedClient>(EventFeed);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
