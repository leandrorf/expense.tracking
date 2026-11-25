using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Projection;
using Expense.Tracking.Reporting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// Factory dos testes: banco da projecao em memoria e o feed de eventos falso. Nao ha client para o
/// transactions nem para o accounts: o resumo so le a projecao (EVT-3).
/// O worker da projecao fica desligado por padrao (os testes chamam o processador); subclasses
/// mudam configuracoes por <see cref="ConfigureTestSettings"/>.
/// </summary>
public class ReportingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"reporting-tests-{Guid.NewGuid():N}";

    public FakeEventFeed EventFeed { get; } = new();

    /// <summary>INV-4: cartoes que o "accounts" devolve, por dono.</summary>
    public CardRegistry Cards { get; } = new();

    /// <summary>NTF-8: e-mails "enviados" (o SMTP nao existe nos testes).</summary>
    public RecordingEmailSender Emails { get; } = new();

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
            ["ConnectionStrings:ReportingDb"] = "server=localhost;database=ignored;user=root;password=root;",
            ["Projection:Enabled"] = "false",
            ["Reminders:Digest:Enabled"] = "false",
            ["App:PublicUrl"] = "http://app.test/",
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
                    d.ServiceType == typeof(IEventFeedClient) ||
                    d.ServiceType == typeof(ICardDirectory) ||
                    d.ServiceType == typeof(Expense.Tracking.Reporting.Application.Reminders.IServiceCardDirectory) ||
                    d.ServiceType == typeof(Expense.Tracking.Reporting.Application.Reminders.IEmailSender) ||
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
            services.AddSingleton<IEventFeedClient>(EventFeed);
            services.AddSingleton(Cards);
            services.AddScoped<ICardDirectory, FakeCardDirectory>();
            services.AddSingleton<Expense.Tracking.Reporting.Application.Reminders.IServiceCardDirectory>(new FakeServiceCardDirectory(Cards));
            services.AddSingleton<Expense.Tracking.Reporting.Application.Reminders.IEmailSender>(Emails);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>Aplica o feed falso na projecao ate esvaziar (o que o worker faria em segundo plano).</summary>
    public async Task DrainFeedAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            await using var scope = Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ProjectionProcessor>()
                .ProcessNextBatchAsync(CancellationToken.None);
            if (result.Read == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("O feed nao esvaziou em 100 lotes.");
    }
}
