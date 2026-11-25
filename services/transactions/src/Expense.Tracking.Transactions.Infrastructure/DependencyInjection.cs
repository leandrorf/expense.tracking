using Expense.Tracking.Transactions.Application.AccountingExport;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.CardPayments;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Imports;
using Expense.Tracking.Transactions.Application.Recurrences;
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
        services.AddScoped<UpdateTransactionHandler>();
        services.AddScoped<RecategorizeTransactionsHandler>();

        // REC (ADR-0008): regras de recorrencia; "hoje" no fuso configurado (o mesmo do reporting).
        services.AddSingleton(LocalCalendar.FromId(configuration["TimeZone"]));
        services.AddScoped<IRecurrenceRepository, RecurrenceRepository>();
        services.AddScoped<CreateRecurrenceHandler>();
        services.AddScoped<ListRecurrencesHandler>();
        services.AddScoped<GetRecurrenceHandler>();
        services.AddScoped<UpdateRecurrenceHandler>();
        services.AddScoped<DeleteRecurrenceHandler>();
        services.AddScoped<SetRecurrenceAmountHandler>();
        services.AddScoped<RemoveRecurrenceAmountHandler>();
        services.AddScoped<ApplyRecurrenceAdjustmentHandler>();
        services.AddScoped<LaunchOccurrenceHandler>();
        services.AddScoped<SkipOccurrenceHandler>();
        services.AddScoped<UnskipOccurrenceHandler>();
        services.AddScoped<ListDueOccurrencesHandler>();

        // RJT-6 (ADR-0010, decisao 6): indices oficiais da API SGS do Banco Central (o worker fica na Api).
        services.AddScoped<IEconomicIndexRepository, EconomicIndexRepository>();
        services.AddScoped<FetchEconomicIndexesJob>();
        services.AddSingleton(IndexFetchOptionsFrom(configuration));
        var indexesBaseUrl = configuration[IndexesBaseUrlKey] ?? "https://api.bcb.gov.br/";
        services.AddHttpClient<IEconomicIndexSource, BcbSgsIndexSource>(client =>
        {
            client.BaseAddress = new Uri(indexesBaseUrl.EndsWith('/') ? indexesBaseUrl : indexesBaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // INV-6: pagamentos de fatura (ADR-0005, decisao 6).
        services.AddScoped<ICardPaymentRepository, CardPaymentRepository>();
        services.AddScoped<RegisterCardPaymentHandler>();
        services.AddScoped<ListCardPaymentsHandler>();
        services.AddScoped<DeleteCardPaymentHandler>();
        services.AddScoped<ExportAccountingHandler>();
        services.AddScoped<ILedgerAccountRepository, LedgerAccountRepository>();
        services.AddScoped<IAccountingExportRepository, AccountingExportRepository>();
        services.AddScoped<ListLedgerAccountsHandler>();
        services.AddScoped<SetLedgerAccountHandler>();
        services.AddScoped<DeleteLedgerAccountHandler>();

        // IMP-3/IMP-4: leitura de CSV, lotes e previa da importacao (ADR-0004).
        services.AddSingleton<ICsvTableReader, CsvHelperTableReader>();
        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddScoped<ICsvMappingRepository, CsvMappingRepository>();
        services.AddScoped<PreviewImportHandler>();
        services.AddScoped<ProposeCsvMappingHandler>();
        services.AddScoped<GetImportHandler>();
        services.AddScoped<ConfirmImportHandler>();
        services.AddScoped<UndoImportHandler>();
        services.AddScoped<ListCsvMappingsHandler>();
        services.AddScoped<SaveCsvMappingHandler>();
        services.AddScoped<DeleteCsvMappingHandler>();
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

    /// <summary>RJT-6: host da API SGS (padrao <c>https://api.bcb.gov.br/</c>).</summary>
    public const string IndexesBaseUrlKey = "Indexes:Fetch:BaseUrl";

    /// <summary>RJT-6: primeiro mes (<c>yyyy-MM</c>) buscado quando o indice esta vazio; padrao: 24 meses atras.</summary>
    public const string IndexesSinceKey = "Indexes:Fetch:Since";

    private static IndexFetchOptions IndexFetchOptionsFrom(IConfiguration configuration)
    {
        var since = configuration[IndexesSinceKey];
        if (string.IsNullOrWhiteSpace(since))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new IndexFetchOptions(new DateOnly(today.Year, today.Month, 1).AddMonths(-24));
        }

        return DateOnly.TryParseExact(since + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var first)
            ? new IndexFetchOptions(first)
            : throw new InvalidOperationException($"{IndexesSinceKey} invalido: use yyyy-MM.");
    }

    /// <summary>Liga/desliga o backfill de escopos dos lancamentos legados (EVT-4). Padrao: ligado.</summary>
    public const string LegacyScopeBackfillEnabledKey = "Events:LegacyScopeBackfill:Enabled";
}
