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
    DateOnly OccurredOn)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionRegistered";

    [JsonIgnore]
    public override string EventType => Name;

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
        transaction.OccurredOn);
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
