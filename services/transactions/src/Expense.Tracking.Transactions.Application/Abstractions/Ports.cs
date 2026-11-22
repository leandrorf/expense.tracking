using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Expense.Tracking.Transactions.Domain.CardPayments;
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

    /// <summary>LNC-7: os lancamentos do dono com estes ids (id de outro dono ou inexistente fica de fora).</summary>
    Task<IReadOnlyList<Transaction>> ListByIdsAsync(Guid ownerUserId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);

    void Remove(Transaction transaction);

    /// <summary>
    /// IMP-6: para cada chave de descricao (<see cref="Transaction.DescriptionKeyOf"/>) com historico,
    /// a categoria do lancamento mais recente do dono, em qualquer conta. Chave sem historico fica de fora.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> FindLatestCategoriesAsync(
        Guid ownerUserId, IReadOnlyCollection<string> descriptionKeys, CancellationToken cancellationToken);

    /// <summary>IMP-9: lancamentos do dono que ainda existem com este lote de origem.</summary>
    Task<IReadOnlyList<Transaction>> ListByImportBatchAsync(Guid ownerUserId, Guid importBatchId, CancellationToken cancellationToken);

    /// <summary>IMP-4: quais das chaves externas ja estao lancadas na conta (deduplicacao da previa).</summary>
    Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
        Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>
    /// CTB-5: a categoria de cada lancamento do dono nas contas (uma por lancamento, sem agrupar: o
    /// banco ignora maiusculas, e a categoria e comparada como foi digitada).
    /// </summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(
        Guid ownerUserId, IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
}

/// <summary>CTB-6: ultima exportacao contabil de cada mes e conta (nulo = todas as contas PJ).</summary>
public interface IAccountingExportRepository
{
    Task<AccountingExportRecord?> FindAsync(Guid ownerUserId, DateOnly month, Guid? accountId, CancellationToken cancellationToken);

    Task AddAsync(AccountingExportRecord record, CancellationToken cancellationToken);
}

/// <summary>CTB-5: plano de contas do usuario (categoria → conta contabil).</summary>
public interface ILedgerAccountRepository
{
    Task<IReadOnlyList<LedgerAccount>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task AddAsync(LedgerAccount account, CancellationToken cancellationToken);

    void Remove(LedgerAccount account);
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

/// <summary>IMP-11: mapeamentos de CSV salvos pelo usuario.</summary>
public interface ICsvMappingRepository
{
    /// <summary>Todos os do dono (no maximo <see cref="SavedCsvMapping.MaxPerUser"/>), por nome.</summary>
    Task<IReadOnlyList<SavedCsvMapping>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task AddAsync(SavedCsvMapping mapping, CancellationToken cancellationToken);

    void Remove(SavedCsvMapping mapping);
}

/// <summary>INV-6: pagamentos de fatura (ADR-0005, decisao 6), sempre escopados pelo dono.</summary>
public interface ICardPaymentRepository
{
    Task<CardPayment?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Do dono, filtrando por cartao e/ou mes da fatura; mais recentes primeiro.</summary>
    Task<IReadOnlyList<CardPayment>> ListAsync(
        Guid ownerUserId, Guid? creditCardId, DateOnly? invoiceDueMonth, CancellationToken cancellationToken);

    Task AddAsync(CardPayment payment, CancellationToken cancellationToken);

    void Remove(CardPayment payment);

    /// <summary>INV-11: quais das chaves externas ja viraram pagamento de fatura na conta (deduplicacao da importacao).</summary>
    Task<IReadOnlySet<string>> FindExistingExternalIdsAsync(
        Guid ownerUserId, Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>INV-11: pagamentos do dono que ainda existem com este lote de origem.</summary>
    Task<IReadOnlyList<CardPayment>> ListByImportBatchAsync(Guid ownerUserId, Guid importBatchId, CancellationToken cancellationToken);

    /// <summary>CTB-3: pagamentos do dono feitos entre as datas (inclusive), pela data do pagamento.</summary>
    Task<IReadOnlyList<CardPayment>> ListPaidBetweenAsync(Guid ownerUserId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    /// <exception cref="PersistenceConflictException">Outro processo gravou antes (chave unica ou concorrencia otimista).</exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Descarta o que foi carregado e alterado nesta unidade de trabalho, para tentar de novo.</summary>
    void Reset();
}

/// <summary>
/// IMP-5: a gravacao perdeu uma corrida, por indice unico (a mesma chave externa importada por
/// outro lote) ou por concorrencia otimista (o mesmo lote confirmado em paralelo). Quem chamou
/// recarrega o estado e decide de novo.
/// </summary>
public sealed class PersistenceConflictException(string message, Exception innerException)
    : Exception(message, innerException);

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

    /// <summary>INV-6: se a conta esta ativa, ou <c>null</c> se ela nao existe ou nao pertence ao usuario.</summary>
    Task<bool?> IsAccountActiveAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>INV-11: cartoes ativos da conta (vazio se nenhum), para sugerir a fatura paga no extrato.</summary>
    Task<IReadOnlyList<InvoiceCardHint>> ListActiveCardsAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>CTB-2: todas as contas do usuario, ativas e inativas (exportacao contabil).</summary>
    Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(CancellationToken cancellationToken);

    /// <summary>CTB-2: todos os cartoes do usuario, ativos e inativos (exportacao contabil).</summary>
    Task<IReadOnlyList<CardInfo>> ListCardsAsync(CancellationToken cancellationToken);
}

/// <summary>CTB-2: o que a exportacao contabil usa da conta. <c>Scope</c> e "Personal" ou "Business".</summary>
public sealed record AccountInfo(Guid Id, string Name, string Scope, string? TaxId, string Currency, bool IsActive)
{
    public const string BusinessScope = "Business";

    public bool IsBusiness => string.Equals(Scope, BusinessScope, StringComparison.OrdinalIgnoreCase);
}

/// <summary>CTB-2: o que a exportacao contabil usa do cartao.</summary>
public sealed record CardInfo(Guid Id, Guid AccountId, string Label, string LastFourDigits);

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
