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

    void SetCheckpoint(long sequence);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
