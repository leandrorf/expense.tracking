using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Transactions;

/// <summary>Value object monetario: valor (positivo) + moeda ISO 4217.</summary>
public sealed class Money : IEquatable<Money>
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string? currency)
    {
        if (amount <= 0)
        {
            return Result.Failure<Money>(Error.Validation("money.amount_invalid", "O valor deve ser maior que zero."));
        }

        // Duas casas decimais.
        if (decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<Money>(Error.Validation("money.amount_precision", "O valor deve ter no maximo 2 casas decimais."));
        }

        var normalized = string.IsNullOrWhiteSpace(currency) ? "BRL" : currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            return Result.Failure<Money>(Error.Validation("money.currency_invalid", "Moeda deve ter 3 letras (ISO 4217)."));
        }

        return new Money(amount, normalized);
    }

    public static Money FromTrusted(decimal amount, string currency) => new(amount, currency);

    public bool Equals(Money? other) => other is not null && Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
