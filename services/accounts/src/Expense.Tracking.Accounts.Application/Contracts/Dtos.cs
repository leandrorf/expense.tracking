using Expense.Tracking.Accounts.Domain.Accounts;
using Expense.Tracking.Accounts.Domain.Cards;

namespace Expense.Tracking.Accounts.Application.Contracts;

public sealed record AccountDto(
    Guid Id,
    string Name,
    string Scope,
    string? TaxId,
    string Currency,
    bool IsActive,
    DateTimeOffset CreatedAtUtc)
{
    public static AccountDto From(Account a) =>
        new(a.Id, a.Name, a.Scope.ToString(), a.TaxId, a.Currency, a.IsActive, a.CreatedAtUtc);
}

public sealed record CreditCardDto(
    Guid Id,
    Guid AccountId,
    string Label,
    string Brand,
    string LastFourDigits,
    decimal CreditLimit,
    int ClosingDay,
    int DueDay,
    bool IsActive,
    DateTimeOffset CreatedAtUtc)
{
    public static CreditCardDto From(CreditCard c) =>
        new(c.Id, c.AccountId, c.Label, c.Brand, c.LastFourDigits, c.CreditLimit, c.ClosingDay, c.DueDay, c.IsActive, c.CreatedAtUtc);
}
