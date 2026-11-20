using Expense.Tracking.Reporting.Application.Projection;
using Expense.Tracking.Reporting.Domain.Projection;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Application.Abstractions;

public sealed record ReportFilter(Guid? AccountId = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>Le os lancamentos do servico transactions (escopados pelo token do usuario).</summary>
public interface ITransactionsClient
{
    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(ReportFilter filter, CancellationToken cancellationToken);
}

/// <summary>Le as contas do servico accounts para mapear conta -> escopo (PF/PJ).</summary>
public interface IAccountsClient
{
    Task<IReadOnlyDictionary<Guid, string>> GetAccountScopesAsync(CancellationToken cancellationToken);
}

/// <summary>Fornece o access token do usuario atual para encaminhar entre servicos.</summary>
public interface IUserTokenAccessor
{
    string? GetAccessToken();
}

/// <summary>Fornece o correlation id da request atual para propagar entre servicos (OBS-1).</summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }
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
