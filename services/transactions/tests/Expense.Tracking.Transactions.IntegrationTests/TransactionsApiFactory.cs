using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// Factory padrao dos testes: EF InMemory, auth de teste e <see cref="FakeAccountsClient"/>.
/// Subclasses podem trocar configuracoes e servicos pelos ganchos protegidos (ex.: OBS-2 usa
/// o client HTTP real contra um accounts falso para observar o trace).
/// </summary>
public class TransactionsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"transactions-tests-{Guid.NewGuid():N}";

    public FakeAccountsClient AccountsClient { get; } = new();

    /// <summary>Quando false, mantem o <c>AccountsHttpClient</c> real registrado pela infraestrutura.</summary>
    protected virtual bool UseFakeAccountsClient => true;

    protected virtual void ConfigureTestSettings(IDictionary<string, string?> settings)
    {
    }

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:TransactionsDb"] = "server=localhost;database=ignored;user=root;password=root;",
            ["Jwt:Authority"] = "http://localhost",
            ["Services:AccountsBaseUrl"] = "http://localhost:5090",
            // Sem margem no feed de eventos: os testes leem o evento logo apos gravar.
            ["Events:FeedSafetyMarginSeconds"] = "0",
            // EVT-4: o worker do backfill chamaria o accounts/identity reais; os testes o chamam direto.
            ["Events:LegacyScopeBackfill:Enabled"] = "false",
            // IMP-4: a limpeza de lotes vencidos e chamada direto pelos testes.
            ["Imports:Cleanup:Enabled"] = "false",
        };
        ConfigureTestSettings(settings);

        // UseSetting (e nao ConfigureAppConfiguration): com minimal hosting, so assim o valor
        // ja esta visivel quando o Program le a configuracao para registrar servicos
        // (ex.: a BaseAddress do client do accounts).
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<TransactionsDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(TransactionsDbContext) ||
                    (UseFakeAccountsClient && d.ServiceType == typeof(IAccountsClient)) ||
                    (d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true
                        && d.ServiceType.GenericTypeArguments.Contains(typeof(TransactionsDbContext))))
                .ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TransactionsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            if (UseFakeAccountsClient)
            {
                services.AddSingleton<IAccountsClient>(AccountsClient);
            }

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            ConfigureTestServices(services);
        });
    }
}
