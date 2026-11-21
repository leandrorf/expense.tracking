using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Imports;
using Expense.Tracking.Transactions.Application.Transactions;
using Expense.Tracking.Transactions.Infrastructure.Auth;
using Expense.Tracking.Transactions.Infrastructure.Clients;
using Expense.Tracking.Transactions.Infrastructure.Imports;
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

        // IMP-3/IMP-4: leitura de CSV, lotes e previa da importacao (ADR-0004).
        services.AddSingleton<ICsvTableReader, CsvHelperTableReader>();
        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddScoped<PreviewImportHandler>();
        services.AddScoped<ProposeCsvMappingHandler>();
        services.AddScoped<GetImportHandler>();
        services.AddScoped<ConfirmImportHandler>();
        services.AddScoped<UndoImportHandler>();
        services.AddSingleton<ImportMetrics>();

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

        AddLegacyScopeBackfill(services, configuration, accountsBaseUrl);

        return services;
    }

    /// <summary>
    /// EVT-4: cliente do endpoint interno de escopos do accounts, com token de servico (client
    /// credentials, escopo <c>accounts.scopes.read</c>). O worker que o usa fica na Api.
    /// </summary>
    private static void AddLegacyScopeBackfill(IServiceCollection services, IConfiguration configuration, string accountsBaseUrl)
    {
        var enabled = configuration.GetValue(LegacyScopeBackfillEnabledKey, true);
        var secret = configuration["ServiceAuth:ClientSecret"];
        if (enabled && string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"ServiceAuth:ClientSecret nao configurado: o backfill de escopos precisa do token de servico ({LegacyScopeBackfillEnabledKey}=false desliga).");
        }

        services.AddSingleton(new ServiceClientOptions(
            TokenEndpoint: new Uri(configuration["ServiceAuth:TokenEndpoint"] ?? "http://localhost:5080/oauth/token"),
            ClientId: configuration["ServiceAuth:ClientId"] ?? "expense-tracking-service",
            ClientSecret: secret ?? string.Empty,
            Scope: configuration["ServiceAuth:Scope"] ?? "accounts.scopes.read"));
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName);
        services.AddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();
        services.AddHttpClient<IAccountScopeDirectory, AccountScopeDirectoryHttpClient>(client =>
        {
            client.BaseAddress = new Uri(accountsBaseUrl.EndsWith('/') ? accountsBaseUrl : accountsBaseUrl + "/");
        })
        .AddHttpMessageHandler<ServiceTokenHandler>();
    }

    /// <summary>Liga/desliga o backfill de escopos dos lancamentos legados (EVT-4). Padrao: ligado.</summary>
    public const string LegacyScopeBackfillEnabledKey = "Events:LegacyScopeBackfill:Enabled";
}
