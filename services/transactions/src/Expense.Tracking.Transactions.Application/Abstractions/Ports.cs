using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Abstractions;

/// <summary>Filtros de consulta de lancamentos (sempre escopados pelo dono).</summary>
public sealed record TransactionQuery(
    Guid OwnerUserId,
    Guid? AccountId = null,
    string? Category = null,
    DateOnly? From = null,
    DateOnly? To = null);

public interface ITransactionRepository
{
    Task<Transaction?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Transaction>> ListAsync(TransactionQuery query, CancellationToken cancellationToken);

    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);

    void Remove(Transaction transaction);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Cliente do servico 'accounts'. Verifica existencia/propriedade (o accounts filtra pelo
/// dono do token encaminhado), permitindo validar referencias sem acessar o banco dele.
/// </summary>
public interface IAccountsClient
{
    /// <summary>
    /// Escopo (PF/PJ) da conta, ou <c>null</c> se ela nao existe ou nao pertence ao usuario.
    /// O escopo e imutavel no accounts, entao pode ser copiado para os eventos (EVT-1).
    /// </summary>
    Task<string?> FindAccountScopeAsync(Guid accountId, CancellationToken cancellationToken);

    Task<bool> CreditCardExistsAsync(Guid creditCardId, CancellationToken cancellationToken);
}

/// <summary>Fornece o access token do usuario atual para encaminhar em chamadas entre servicos.</summary>
public interface IUserTokenAccessor
{
    string? GetAccessToken();
}

/// <summary>Fornece o correlation id da request atual para propagar em chamadas entre servicos (OBS-1).</summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}

/// <summary>
/// EVT-1: outbox transacional. O evento adicionado e gravado no mesmo commit da mudanca
/// (mesmo <see cref="IUnitOfWork"/>), entao nunca existe lancamento sem evento nem o contrario.
/// </summary>
public interface IEventOutbox
{
    void Add(TransactionEvent @event);
}

/// <summary>Evento gravado no outbox, como servido pelo feed (payload em JSON).</summary>
public sealed record FeedEvent(
    long Sequence,
    Guid EventId,
    string EventType,
    int Version,
    Guid AggregateId,
    Guid OwnerUserId,
    DateTimeOffset OccurredAtUtc,
    string? TraceParent,
    string Payload);

/// <summary>Leitura do outbox em ordem de sequencia (EVT-1, feed <c>GET /events</c>).</summary>
public interface IEventFeed
{
    Task<IReadOnlyList<FeedEvent>> ReadAfterAsync(long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>Maior sequencia gravada no outbox (0 se vazio), inclusive eventos ainda na margem.</summary>
    Task<long> GetHeadSequenceAsync(CancellationToken cancellationToken);
}
