using Expense.Tracking.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Identity.IntegrationTests;

/// <summary>
/// Sobe a API em memoria para testes de integracao, substituindo o MySQL por EF InMemory. Os clientes
/// OAuth, o usuario demo, o CORS e a dica da tela de login vem da configuracao (IDX-2), como no
/// compose do monorepo; <see cref="Settings"/> permite trocar ou remover qualquer um deles.
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    public const string SpaClientId = "expense-tracking-spa";
    public const string SpaRedirectUri = "http://localhost:5173/callback";
    public const string SpaOrigin = "http://localhost:5173";
    public const string ServiceClientId = "expense-tracking-service";
    public const string ServiceClientSecret = "dev-service-secret";
    public const string DemoEmail = "demo@expense.local";
    public const string DemoPassword = "Password123!";
    public const string LoginHint = "Demo: demo@expense.local / Password123!";

    /// <summary>Configuracao padrao dos testes: a mesma que o monorepo declara para o Identity.</summary>
    public static Dictionary<string, string?> DefaultSettings() => new()
    {
        ["Seed:Enabled"] = "true",
        [$"Seed:Clients:{SpaClientId}:DisplayName"] = "Expense Tracking SPA",
        [$"Seed:Clients:{SpaClientId}:Type"] = "Public",
        [$"Seed:Clients:{SpaClientId}:RedirectUris:0"] = SpaRedirectUri,
        [$"Seed:Clients:{SpaClientId}:Scopes:0"] = "openid",
        [$"Seed:Clients:{SpaClientId}:Scopes:1"] = "profile",
        [$"Seed:Clients:{SpaClientId}:Scopes:2"] = "offline_access",
        [$"Seed:Clients:{SpaClientId}:Scopes:3"] = "expenses.read",
        [$"Seed:Clients:{SpaClientId}:Scopes:4"] = "expenses.write",
        [$"Seed:Clients:{SpaClientId}:GrantTypes:0"] = "authorization_code",
        [$"Seed:Clients:{SpaClientId}:GrantTypes:1"] = "refresh_token",
        [$"Seed:Clients:{ServiceClientId}:Type"] = "Confidential",
        [$"Seed:Clients:{ServiceClientId}:Secret"] = ServiceClientSecret,
        [$"Seed:Clients:{ServiceClientId}:Scopes:0"] = "expenses.read",
        [$"Seed:Clients:{ServiceClientId}:Scopes:1"] = "expenses.write",
        [$"Seed:Clients:{ServiceClientId}:Scopes:2"] = "events.read",
        [$"Seed:Clients:{ServiceClientId}:Scopes:3"] = "accounts.scopes.read",
        [$"Seed:Clients:{ServiceClientId}:GrantTypes:0"] = "client_credentials",
        ["Seed:Users:demo:Email"] = DemoEmail,
        ["Seed:Users:demo:Password"] = DemoPassword,
        ["Seed:Users:demo:DisplayName"] = "Demo User",
        ["Cors:AllowedOrigins:0"] = SpaOrigin,
        ["LoginPage:Hint"] = LoginHint,
    };

    // Raiz unica dos bancos InMemory: dois factories com o mesmo nome de banco enxergam os mesmos dados.
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    private readonly string _databaseName;

    public IdentityApiFactory()
        : this(DefaultSettings())
    {
    }

    /// <param name="databaseName">Banco InMemory; repetir o nome simula reiniciar sobre o mesmo banco.</param>
    internal IdentityApiFactory(Dictionary<string, string?> settings, string? databaseName = null)
    {
        Settings = settings;
        _databaseName = databaseName ?? $"identity-tests-{Guid.NewGuid():N}";
    }

    public Dictionary<string, string?> Settings { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDb"] = "server=localhost;database=ignored;user=root;password=root;",
                ["OAuth:Issuer"] = "http://localhost",
                ["OAuth:DefaultAudience"] = "expense-tracking-api",
            });
        });

        // UseSetting (e nao ConfigureAppConfiguration): o Program le o CORS ainda no builder, antes de
        // a configuracao da factory valer.
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<IdentityDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(IdentityDbContext) ||
                    // EF Core 9+/10 aplica TODAS as IDbContextOptionsConfiguration<T> registradas;
                    // removemos a do MySQL para que so o InMemory valha.
                    (d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true
                        && d.ServiceType.GenericTypeArguments.Contains(typeof(IdentityDbContext))))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName, DatabaseRoot));
        });
    }
}
