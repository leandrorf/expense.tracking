using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Reports;
using Expense.Tracking.Reporting.Infrastructure.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    private static Uri ToBaseUri(string url) => new(url.EndsWith('/') ? url : url + "/");
}
