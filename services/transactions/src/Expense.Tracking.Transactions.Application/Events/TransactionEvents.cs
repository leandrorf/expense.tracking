using System.Text.Json.Serialization;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Events;

/// <summary>
/// EVT-1 (ADR-0001): eventos de integracao publicados pelo transactions no outbox e servidos no
/// feed <c>GET /events</c>. Sao contratos publicos: mudanca incompativel exige nova
/// <see cref="Version"/>. O envelope (sequencia, id, horario, traceparent) e montado pelo outbox.
/// </summary>
public abstract record TransactionEvent(Guid TransactionId, Guid OwnerUserId)
{
    /// <summary>Nome do evento no feed (ex.: <c>TransactionRegistered</c>).</summary>
    [JsonIgnore]
    public abstract string EventType { get; }

    /// <summary>Versao do formato do payload.</summary>
    [JsonIgnore]
    public virtual int Version => 1;
}

/// <summary>
/// Um lancamento foi criado. Carrega o que a projecao do reporting precisa, incluindo o escopo
/// PF/PJ da conta (imutavel no accounts). <see cref="AccountScope"/> e nulo so nos eventos da
/// carga inicial de lancamentos anteriores ao outbox, cujo escopo o transactions nao conhece.
/// <para>
/// Versao 2 (INV-2, ADR-0005, decisao 2): acrescenta <see cref="Description"/>,
/// <see cref="CreditCardId"/> (nulo fora do cartao) e <see cref="Installments"/> (1 a vista), para a
/// fatura do cartao. Os eventos v1 ja gravados continuam no feed como estao.
/// </para>
/// </summary>
public sealed record TransactionRegistered(
    Guid TransactionId,
    Guid OwnerUserId,
    Guid AccountId,
    string? AccountScope,
    string TransactionType,
    string PaymentMethod,
    string Category,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn,
    string Description,
    Guid? CreditCardId,
    int Installments)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionRegistered";

    public const int CurrentVersion = 2;

    [JsonIgnore]
    public override string EventType => Name;

    [JsonIgnore]
    public override int Version => CurrentVersion;

    public static TransactionRegistered From(Transaction transaction, string? accountScope) => new(
        transaction.Id,
        transaction.OwnerUserId,
        transaction.AccountId,
        accountScope,
        transaction.Type.ToString(),
        transaction.PaymentMethod.ToString(),
        transaction.Category,
        transaction.Amount,
        transaction.Currency,
        transaction.OccurredOn,
        transaction.Description,
        transaction.CreditCardId,
        transaction.Installments);
}

/// <summary>
/// INV-2 (ADR-0005, decisao 2): descricao, cartao e parcelas de uma compra no cartao cujo
/// <see cref="TransactionRegistered"/> saiu na versao 1, sem esses campos. Completa o evento sem
/// reescrever o log, como o <see cref="TransactionAccountScopeResolved"/>. Consumidores aplicam so
/// se o lancamento ainda existir.
/// </summary>
public sealed record TransactionCardDetailsResolved(
    Guid TransactionId,
    Guid OwnerUserId,
    string Description,
    Guid CreditCardId,
    int Installments)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionCardDetailsResolved";

    [JsonIgnore]
    public override string EventType => Name;
}

/// <summary>
/// EVT-4: o escopo PF/PJ da conta de um lancamento anterior ao outbox foi descoberto (o
/// <see cref="TransactionRegistered"/> dele saiu com <c>accountScope = null</c>). Completa esse
/// evento sem reescrever o log: quem reconstroi a projecao desde a sequencia 0 chega ao mesmo
/// escopo. Consumidores aplicam so se o lancamento ainda existir.
/// </summary>
public sealed record TransactionAccountScopeResolved(
    Guid TransactionId,
    Guid OwnerUserId,
    Guid AccountId,
    string AccountScope)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionAccountScopeResolved";

    [JsonIgnore]
    public override string EventType => Name;
}

/// <summary>Um lancamento foi excluido (a exclusao e fisica; este evento e o unico registro dela).</summary>
public sealed record TransactionDeleted(Guid TransactionId, Guid OwnerUserId)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionDeleted";

    [JsonIgnore]
    public override string EventType => Name;
}
