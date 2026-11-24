using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Application.Reminders;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Application.Projection;
using Expense.Tracking.Reporting.Application.Reports;
using Expense.Tracking.Reporting.Infrastructure.Auth;
using Expense.Tracking.Reporting.Infrastructure.Clients;
using Expense.Tracking.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Expense.Tracking.Reporting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// EVT-2/EVT-3 (ADR-0001): projecao alimentada pelo feed de eventos do transactions, no banco
    /// proprio <c>expense_reporting</c>, e o resumo lido dela. O worker que chama o processador
    /// fica na Api.
    /// </summary>
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ReportingDb")
            ?? throw new InvalidOperationException("Connection string 'ReportingDb' nao configurada.");
        services.AddDbContext<ReportingDbContext>(options => options.UseMySQL(connectionString));
        services.AddScoped<IProjectionStore, ProjectionStore>();
        services.AddScoped<IProjectionQueries, ProjectionQueries>();
        services.AddScoped<GetSummaryHandler>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<CreateBudgetHandler>();
        services.AddScoped<ListBudgetsHandler>();
        services.AddScoped<ChangeBudgetHandler>();
        services.AddScoped<GetBudgetStatusHandler>();
        services.AddScoped<GetCategorySuggestionsHandler>();
        services.AddScoped<GetCardInvoicesHandler>();
        services.AddScoped<GetCardLimitsHandler>();
        services.AddScoped<GetCashForecastHandler>();
        services.AddScoped<GetRemindersHandler>();
        services.AddScoped<IReminderStore, ReminderStore>();
        services.AddScoped<DismissReminderHandler>();
        services.AddSingleton(new InvoiceOptions(ResolveTimeZone(configuration["Reports:TimeZone"])));
        services.AddSingleton<ProjectionStatus>();

        var projection = new ProjectionOptions(
            BatchSize: configuration.GetValue("Projection:BatchSize", ProjectionOptions.Default.BatchSize),
            PollInterval: TimeSpan.FromSeconds(configuration.GetValue("Projection:PollIntervalSeconds", ProjectionOptions.Default.PollInterval.TotalSeconds)),
            Enabled: configuration.GetValue("Projection:Enabled", ProjectionOptions.Default.Enabled));
        services.AddSingleton(projection);
        services.AddScoped<ProjectionProcessor>();

        // Token de servico (client credentials) para ler o feed sem usuario.
        var secret = configuration["ServiceAuth:ClientSecret"];
        if (projection.Enabled && string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "ServiceAuth:ClientSecret nao configurado: o worker da projecao precisa do token de servico.");
        }

        services.AddSingleton(new ServiceClientOptions(
            TokenEndpoint: new Uri(configuration["ServiceAuth:TokenEndpoint"] ?? "http://localhost:5080/oauth/token"),
            ClientId: configuration["ServiceAuth:ClientId"] ?? "expense-tracking-service",
            ClientSecret: secret ?? string.Empty,
            Scope: configuration["ServiceAuth:Scope"] ?? "events.read"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName);
        services.AddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();

        var transactionsBaseUrl = configuration["Services:TransactionsBaseUrl"] ?? "http://localhost:5100";
        services.AddHttpClient<IEventFeedClient, EventFeedHttpClient>(client =>
            client.BaseAddress = ToBaseUri(transactionsBaseUrl))
            .AddHttpMessageHandler<ServiceTokenHandler>();

        // INV-4 (ADR-0005, decisao 3): ciclo do cartao lido no accounts com o token do usuario.
        var accountsBaseUrl = configuration["Services:AccountsBaseUrl"] ?? "http://localhost:5090";
        services.AddHttpClient<ICardDirectory, AccountsCardDirectory>(client =>
            client.BaseAddress = ToBaseUri(accountsBaseUrl));

        return services;
    }

    /// <summary>
    /// Fuso do "hoje" das faturas; padrao Sao Paulo. Aceita id IANA ou do Windows; sem o fuso na
    /// maquina (ex.: Windows sem ICU), cai para UTC e a situacao pode virar ate 3h antes.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        id ??= "America/Sao_Paulo";
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        return TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) &&
               TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)
            ? zone
            : TimeZoneInfo.Utc;
    }

    private static Uri ToBaseUri(string url) => new(url.EndsWith('/') ? url : url + "/");
}
