using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.CardPayments;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Application.CardPayments;

/// <summary>INV-6: pagamento de fatura, como a API devolve. O mes da fatura vai como "yyyy-MM".</summary>
public sealed record CardPaymentDto(
    Guid Id,
    Guid CreditCardId,
    string InvoiceDueMonth,
    Guid AccountId,
    decimal Amount,
    string Currency,
    DateOnly PaidOn,
    DateTimeOffset CreatedAtUtc)
{
    public static CardPaymentDto From(CardPayment payment) => new(
        payment.Id,
        payment.CreditCardId,
        payment.InvoiceDueMonth.ToString(CardInvoicePaid.MonthFormat, System.Globalization.CultureInfo.InvariantCulture),
        payment.AccountId,
        payment.Amount,
        payment.Currency,
        payment.PaidOn,
        payment.CreatedAtUtc);
}

public sealed record RegisterCardPaymentCommand(
    Guid OwnerUserId,
    Guid CreditCardId,
    DateOnly InvoiceDueMonth,
    Guid AccountId,
    decimal Amount,
    string? Currency,
    DateOnly PaidOn);

/// <summary>
/// INV-6 (ADR-0005, decisao 6): registra o pagamento de uma fatura com o <see cref="CardInvoicePaid"/>
/// no mesmo commit. Cartao e conta sao validados no accounts com o token do usuario (de outro
/// usuario = 404); a conta de origem precisa estar ativa.
/// </summary>
public sealed class RegisterCardPaymentHandler(
    ICardPaymentRepository payments,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<CardPaymentDto>> HandleAsync(RegisterCardPaymentCommand command, CancellationToken cancellationToken)
    {
        var created = CardPayment.Create(
            command.OwnerUserId, command.CreditCardId, command.InvoiceDueMonth, command.AccountId,
            command.Amount, command.Currency, command.PaidOn, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<CardPaymentDto>(created.Error);
        }

        if (!await accountsClient.CreditCardExistsAsync(command.CreditCardId, cancellationToken))
        {
            return Result.Failure<CardPaymentDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        switch (await accountsClient.IsAccountActiveAsync(command.AccountId, cancellationToken))
        {
            case null:
                return Result.Failure<CardPaymentDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
            case false:
                return Result.Failure<CardPaymentDto>(Error.Validation(
                    "card_payment.account_inactive", "A conta de origem do pagamento esta desativada."));
        }

        await payments.AddAsync(created.Value, cancellationToken);
        outbox.Add(CardInvoicePaid.From(created.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CardPaymentDto.From(created.Value);
    }
}

public sealed class ListCardPaymentsHandler(ICardPaymentRepository payments)
{
    public async Task<IReadOnlyList<CardPaymentDto>> HandleAsync(
        Guid ownerUserId, Guid? creditCardId, DateOnly? invoiceDueMonth, CancellationToken cancellationToken) =>
        (await payments.ListAsync(ownerUserId, creditCardId, invoiceDueMonth, cancellationToken))
            .Select(CardPaymentDto.From)
            .ToList();
}

/// <summary>INV-6: exclui um pagamento registrado por engano; a fatura volta a situacao anterior.</summary>
public sealed class DeleteCardPaymentHandler(
    ICardPaymentRepository payments,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetAsync(id, ownerUserId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("card_payment.not_found", "Pagamento de fatura nao encontrado."));
        }

        payments.Remove(payment);
        outbox.Add(new CardInvoicePaymentDeleted(payment.Id, payment.OwnerUserId, payment.CreditCardId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
