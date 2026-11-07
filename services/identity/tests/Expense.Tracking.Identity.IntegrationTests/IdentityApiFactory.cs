using Expense.Tracking.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Identity.IntegrationTests;

/// <summary>
/// Sobe a API em memoria para testes de integracao, substituindo o MySQL por EF InMemory
/// e habilitando o seed de dados (clientes OAuth + usuario demo).
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"identity-tests-{Guid.NewGuid():N}";

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
                ["Seed:Enabled"] = "true"
            });
        });

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

            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
