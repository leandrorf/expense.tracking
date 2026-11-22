using System.Text.Json.Serialization;
using Expense.Tracking.Transactions.Domain.CardPayments;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Events;

/// <summary>
/// EVT-1 (ADR-0001): eventos de integracao publicados pelo transactions no outbox e servidos no
/// feed <c>GET /events</c>. Sao contratos publicos: mudanca incompativel exige nova
/// <see cref="Version"/>. O envelope (sequencia, id, horario, traceparent) e montado pelo outbox.
/// </summary>
public abstract record TransactionEvent(Guid TransactionId, Guid OwnerUserId)
{
    /// <summary>Agregado do evento no outbox. Virtual para o pagamento de fatura tira-lo do payload.</summary>
    public virtual Guid TransactionId { get; init; } = TransactionId;

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

/// <summary>
/// LNC-3 (ADR-0007, decisao 1): um lancamento foi corrigido. Leva o estado completo depois da edicao
/// e a versao do lancamento (<see cref="TransactionVersion"/>, que nao e a <see cref="TransactionEvent.Version"/>
/// do formato do evento); nao leva o escopo, porque a conta nao muda. Consumidores aplicam so se o lancamento
/// ainda existir (uma edicao lida depois da exclusao nao o recria).
/// </summary>
public sealed record TransactionUpdated(
    Guid TransactionId,
    Guid OwnerUserId,
    Guid AccountId,
    string TransactionType,
    string PaymentMethod,
    string Category,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn,
    string Description,
    Guid? CreditCardId,
    int Installments,
    int TransactionVersion)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionUpdated";

    [JsonIgnore]
    public override string EventType => Name;

    public static TransactionUpdated From(Transaction transaction) => new(
        transaction.Id,
        transaction.OwnerUserId,
        transaction.AccountId,
        transaction.Type.ToString(),
        transaction.PaymentMethod.ToString(),
        transaction.Category,
        transaction.Amount,
        transaction.Currency,
        transaction.OccurredOn,
        transaction.Description,
        transaction.CreditCardId,
        transaction.Installments,
        transaction.Version);
}

/// <summary>Um lancamento foi excluido (a exclusao e fisica; este evento e o unico registro dela).</summary>
public sealed record TransactionDeleted(Guid TransactionId, Guid OwnerUserId)
    : TransactionEvent(TransactionId, OwnerUserId)
{
    public const string Name = "TransactionDeleted";

    [JsonIgnore]
    public override string EventType => Name;
}

/// <summary>
/// INV-6 (ADR-0005, decisao 6): base dos eventos de pagamento de fatura. O agregado do outbox e o
/// pagamento (<see cref="PaymentId"/>), nao um lancamento: o <c>transactionId</c> da base fica fora
/// do payload para ninguem confundir o pagamento com um lancamento.
/// </summary>
public abstract record CardPaymentEvent(Guid PaymentId, Guid OwnerUserId)
    : TransactionEvent(PaymentId, OwnerUserId)
{
    [JsonIgnore]
    public override Guid TransactionId
    {
        get => base.TransactionId;
        init => base.TransactionId = value;
    }
}

/// <summary>Um pagamento de fatura foi registrado. Nunca e gasto: so abate a fatura do mes de vencimento.</summary>
public sealed record CardInvoicePaid(
    Guid PaymentId,
    Guid OwnerUserId,
    Guid CreditCardId,
    string InvoiceDueMonth,
    Guid AccountId,
    decimal Amount,
    string Currency,
    DateOnly PaidOn)
    : CardPaymentEvent(PaymentId, OwnerUserId)
{
    public const string Name = "CardInvoicePaid";

    /// <summary>Formato do mes da fatura no payload, como na API de faturas do reporting.</summary>
    public const string MonthFormat = "yyyy-MM";

    [JsonIgnore]
    public override string EventType => Name;

    public static CardInvoicePaid From(CardPayment payment) => new(
        payment.Id,
        payment.OwnerUserId,
        payment.CreditCardId,
        payment.InvoiceDueMonth.ToString(MonthFormat, System.Globalization.CultureInfo.InvariantCulture),
        payment.AccountId,
        payment.Amount,
        payment.Currency,
        payment.PaidOn);
}

/// <summary>Um pagamento de fatura foi excluido (exclusao fisica; a fatura volta a situacao anterior).</summary>
public sealed record CardInvoicePaymentDeleted(Guid PaymentId, Guid OwnerUserId, Guid CreditCardId)
    : CardPaymentEvent(PaymentId, OwnerUserId)
{
    public const string Name = "CardInvoicePaymentDeleted";

    [JsonIgnore]
    public override string EventType => Name;
}
