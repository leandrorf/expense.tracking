using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
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

    /// <summary>IMP-4: quais das chaves externas ja estao lancadas na conta (deduplicacao da previa).</summary>
    Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
        Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);
}

/// <summary>IMP-4: lotes de importacao (previa guardada no servidor; ADR-0004, decisao 2).</summary>
public interface IImportBatchRepository
{
    Task AddAsync(ImportBatch batch, CancellationToken cancellationToken);

    /// <summary>Lote do dono com as linhas; lote de outro usuario volta nulo (404).</summary>
    Task<ImportBatch?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Apaga lotes pendentes vencidos e devolve quantos foram apagados.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// EVT-4: escopo PF/PJ de contas no accounts, com token de servico (sem usuario). Usado so para
/// completar os eventos de lancamentos anteriores ao outbox. Ids desconhecidos ficam de fora.
/// </summary>
public interface IAccountScopeDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetScopesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
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

/// <summary>
/// IMP-3: separa os bytes de um CSV em celulas (codificacao, separador, aspas). Sem I/O: o arquivo
/// ja esta em memoria. Erros nunca citam o conteudo do arquivo (ADR-0004, decisao 8).
/// </summary>
public interface ICsvTableReader
{
    /// <summary>Mais linhas nao vazias que isso (cabecalho incluido) = <c>import.too_many_lines</c>.</summary>
    const int MaxRows = ImportBatch.MaxLines + 1;

    /// <summary>Um extrato tem poucas colunas; muitas indicam arquivo errado ou hostil.</summary>
    const int MaxColumns = 50;

    Result<CsvTable> Read(ReadOnlySpan<byte> content);
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
