using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Contracts;

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    string Type,
    string PaymentMethod,
    Guid? CreditCardId,
    string Description,
    string Category,
    decimal Amount,
    string Currency,
    DateOnly OccurredOn,
    string? Notes,
    DateTimeOffset CreatedAtUtc)
{
    public static TransactionDto From(Transaction t) =>
        new(t.Id, t.AccountId, t.Type.ToString(), t.PaymentMethod.ToString(), t.CreditCardId,
            t.Description, t.Category, t.Amount, t.Currency, t.OccurredOn, t.Notes, t.CreatedAtUtc);
}
