using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.CardPayments;

/// <summary>
/// INV-6 (ADR-0005, decisao 6): pagamento de uma fatura de cartao. Nunca e lancamento: nao entra no
/// Resumo, nos orcamentos nem nas sugestoes de categoria, so abate a fatura que paga. Pagamento
/// parcial e acima do total sao aceitos. Nao ha edicao: errou, exclui e registra de novo.
/// </summary>
public sealed class CardPayment
{
    private CardPayment(
        Guid id, Guid ownerUserId, Guid creditCardId, DateOnly invoiceDueMonth, Guid accountId,
        decimal amount, string currency, DateOnly paidOn, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        CreditCardId = creditCardId;
        InvoiceDueMonth = invoiceDueMonth;
        AccountId = accountId;
        Amount = amount;
        Currency = currency;
        PaidOn = paidOn;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid CreditCardId { get; private set; }

    /// <summary>Fatura paga, pelo mes de vencimento (sempre o dia 1 do mes).</summary>
    public DateOnly InvoiceDueMonth { get; private set; }

    /// <summary>Conta de onde saiu o dinheiro (qualquer conta ativa do dono).</summary>
    public Guid AccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly PaidOn { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>INV-11: chave da linha no extrato importado (unica por dono e conta); nula se digitado.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>INV-11: lote de importacao que criou o pagamento, para desfazer a importacao.</summary>
    public Guid? ImportBatchId { get; private set; }

    public static Result<CardPayment> Create(
        Guid ownerUserId, Guid creditCardId, DateOnly invoiceDueMonth, Guid accountId,
        decimal amount, string? currency, DateOnly paidOn, DateTimeOffset nowUtc)
    {
        if (ownerUserId == Guid.Empty || creditCardId == Guid.Empty || accountId == Guid.Empty)
        {
            return Result.Failure<CardPayment>(Error.Validation(
                "card_payment.references_required", "Dono, cartao e conta sao obrigatorios."));
        }

        var money = Money.Create(amount, currency);
        if (money.IsFailure)
        {
            return Result.Failure<CardPayment>(money.Error);
        }

        return new CardPayment(
            Guid.NewGuid(), ownerUserId, creditCardId, new DateOnly(invoiceDueMonth.Year, invoiceDueMonth.Month, 1),
            accountId, money.Value.Amount, money.Value.Currency, paidOn, nowUtc);
    }

    /// <summary>
    /// INV-11: pagamento criado pela confirmacao de um extrato importado, com a chave da linha e o
    /// lote de origem. Mesmas regras de <see cref="Create"/>.
    /// </summary>
    public static Result<CardPayment> CreateImported(
        Guid ownerUserId, Guid creditCardId, DateOnly invoiceDueMonth, Guid accountId, decimal amount, string currency,
        DateOnly paidOn, string externalId, Guid importBatchId, DateTimeOffset nowUtc)
    {
        var result = Create(ownerUserId, creditCardId, invoiceDueMonth, accountId, amount, currency, paidOn, nowUtc);
        if (result.IsSuccess)
        {
            result.Value.ExternalId = externalId;
            result.Value.ImportBatchId = importBatchId;
        }

        return result;
    }
}
