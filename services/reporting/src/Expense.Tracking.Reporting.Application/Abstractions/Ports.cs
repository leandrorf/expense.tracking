using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Application.Projection;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Projection;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Application.Abstractions;

public sealed record ReportFilter(Guid? AccountId = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// EVT-3: le os lancamentos de um usuario na projecao local. O resumo nao chama o transactions
/// nem o accounts na hora da consulta.
/// </summary>
public interface IProjectionQueries
{
    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(Guid ownerUserId, ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>BDG-7: categorias dos lancamentos do dono, como digitadas, com quantas vezes cada uma.</summary>
    Task<IReadOnlyList<CategoryUsage>> GetCategoryUsageAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>INV-4: compras e estornos de um cartao do dono.</summary>
    Task<IReadOnlyList<CardTransactionRecord>> GetCardTransactionsAsync(Guid ownerUserId, Guid cardId, CancellationToken cancellationToken);

    /// <summary>INV-6: pagamentos das faturas de um cartao do dono.</summary>
    Task<IReadOnlyList<CardPaymentRecord>> GetCardPaymentsAsync(Guid ownerUserId, Guid cardId, CancellationToken cancellationToken);

    /// <summary>INV-8: lancamentos do dono fora do cartao, entre as datas (inclusive).</summary>
    Task<IReadOnlyList<CashMovementRecord>> GetCashMovementsAsync(
        Guid ownerUserId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// INV-4 (ADR-0005, decisao 3): cartao do usuario no accounts, lido na hora com o token dele.
/// Nulo quando o cartao nao existe ou nao e do usuario.
/// </summary>
public interface ICardDirectory
{
    Task<CardInfo?> FindAsync(Guid cardId, CancellationToken cancellationToken);

    /// <summary>INV-8: todos os cartoes do usuario (ativos ou nao), com o escopo da conta de cada um.</summary>
    Task<IReadOnlyList<ScopedCard>> ListWithScopesAsync(CancellationToken cancellationToken);

    /// <summary>INV-13: as contas do usuario (ativas ou nao), com o escopo e o saldo informado.</summary>
    Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(CancellationToken cancellationToken);
}

/// <summary>Access token do usuario do request atual, para encaminhar ao accounts.</summary>
public interface IUserTokenAccessor
{
    string? GetAccessToken();
}

/// <summary>EVT-2: le o feed de eventos do transactions (token de servico, escopo <c>events.read</c>).</summary>
public interface IEventFeedClient
{
    Task<FeedPage> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// EVT-2: armazenamento da projecao e do checkpoint do feed. As mudancas so sao gravadas em
/// <see cref="SaveChangesAsync"/>, num unico commit (projecao + checkpoint juntos).
/// </summary>
public interface IProjectionStore
{
    /// <summary>Ultima sequencia do feed ja aplicada (0 se nenhuma).</summary>
    Task<long> GetCheckpointAsync(CancellationToken cancellationToken);

    Task<ProjectedTransaction?> FindAsync(Guid transactionId, CancellationToken cancellationToken);

    void Add(ProjectedTransaction transaction);

    void Remove(ProjectedTransaction transaction);

    /// <summary>INV-6: pagamento de fatura projetado, inclusive um adicionado no lote atual.</summary>
    Task<ProjectedCardPayment?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken);

    void AddPayment(ProjectedCardPayment payment);

    void RemovePayment(ProjectedCardPayment payment);

    void SetCheckpoint(long sequence);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
