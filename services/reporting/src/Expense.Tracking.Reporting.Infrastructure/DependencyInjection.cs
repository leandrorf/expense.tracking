using Expense.Tracking.Reporting.Application.Abstractions;
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
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var transactionsBaseUrl = configuration["Services:TransactionsBaseUrl"] ?? "http://localhost:5100";
        var accountsBaseUrl = configuration["Services:AccountsBaseUrl"] ?? "http://localhost:5090";

        services.AddTransient<CorrelationIdForwardingHandler>();

        services.AddHttpClient<ITransactionsClient, TransactionsHttpClient>(client =>
            client.BaseAddress = ToBaseUri(transactionsBaseUrl))
            .AddHttpMessageHandler<CorrelationIdForwardingHandler>();

        services.AddHttpClient<IAccountsClient, AccountsHttpClient>(client =>
            client.BaseAddress = ToBaseUri(accountsBaseUrl))
            .AddHttpMessageHandler<CorrelationIdForwardingHandler>();

        services.AddScoped<GetSummaryHandler>();

        return services;
    }

    /// <summary>
    /// EVT-2 (ADR-0001): projecao alimentada pelo feed de eventos do transactions, no banco
    /// proprio <c>expense_reporting</c>. O worker que chama o processador fica na Api.
    /// </summary>
    public static IServiceCollection AddReportingProjection(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ReportingDb")
            ?? throw new InvalidOperationException("Connection string 'ReportingDb' nao configurada.");
        services.AddDbContext<ReportingDbContext>(options => options.UseMySQL(connectionString));
        services.AddScoped<IProjectionStore, ProjectionStore>();

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

        return services;
    }

    private static Uri ToBaseUri(string url) => new(url.EndsWith('/') ? url : url + "/");
}
