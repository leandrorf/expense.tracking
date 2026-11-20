using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Transactions;
using Expense.Tracking.Transactions.Infrastructure.Clients;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Transactions.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTransactionsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TransactionsDb")
            ?? throw new InvalidOperationException("Connection string 'TransactionsDb' nao configurada.");

        services.AddDbContext<TransactionsDbContext>(options => options.UseMySQL(connectionString));

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<CreateTransactionHandler>();
        services.AddScoped<ListTransactionsHandler>();
        services.AddScoped<GetTransactionHandler>();
        services.AddScoped<DeleteTransactionHandler>();

        // EVT-1: outbox transacional + feed de eventos (ADR-0001).
        services.AddScoped<IEventOutbox, EventOutbox>();
        services.AddScoped<IEventFeed, EventFeed>();
        services.AddScoped<ReadEventFeedHandler>();
        var marginSeconds = configuration.GetValue<double?>("Events:FeedSafetyMarginSeconds");
        services.AddSingleton(new EventFeedOptions(
            marginSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : EventFeedOptions.DefaultSafetyMargin));

        var accountsBaseUrl = configuration["Services:AccountsBaseUrl"] ?? "http://localhost:5090";
        services.AddTransient<CorrelationIdForwardingHandler>();
        services.AddHttpClient<IAccountsClient, AccountsHttpClient>(client =>
        {
            client.BaseAddress = new Uri(accountsBaseUrl.EndsWith('/') ? accountsBaseUrl : accountsBaseUrl + "/");
        })
        .AddHttpMessageHandler<CorrelationIdForwardingHandler>();

        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}
