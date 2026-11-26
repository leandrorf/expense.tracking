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
    DateTimeOffset CreatedAtUtc,
    decimal? OpeningBalance,
    DateOnly? OpeningBalanceOn)
{
    public static AccountDto From(Account a) =>
        new(a.Id, a.Name, a.Scope.ToString(), a.TaxId, a.Currency, a.IsActive, a.CreatedAtUtc,
            a.OpeningBalance, a.OpeningBalanceOn);
}

/// <summary>CYC-3 (ADR-0012, decisao 5): um ciclo do cartao; <c>fromDueMonth</c> nulo = o da criacao, desde sempre.</summary>
public sealed record CardCycleDto(string? FromDueMonth, int ClosingDay, int DueDay)
{
    public static IReadOnlyList<CardCycleDto> ListOf(CreditCard c) => c.Cycles.Terms
        .Select(t => new CardCycleDto(t.FromDueMonth?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), t.ClosingDay, t.DueDay))
        .ToList();
}

/// <param name="ClosingDay">CYC-3: o fechamento do ciclo de hoje (o da fatura aberta).</param>
/// <param name="DueDay">CYC-3: o vencimento do ciclo de hoje.</param>
/// <param name="Cycles">CYC-3: todos os ciclos, com vigencia.</param>
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
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<CardCycleDto> Cycles)
{
    public static CreditCardDto From(CreditCard c, DateOnly today)
    {
        var current = c.CurrentCycle(today);
        return new(c.Id, c.AccountId, c.Label, c.Brand, c.LastFourDigits, c.CreditLimit, current.ClosingDay, current.DueDay,
            c.IsActive, c.CreatedAtUtc, CardCycleDto.ListOf(c));
    }
}
