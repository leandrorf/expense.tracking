using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Budgets;

/// <summary>Escopo do orcamento: o mesmo enum de escopo de conta do accounts.</summary>
public enum BudgetScope
{
    Personal = 1,
    Business = 2
}

/// <summary>
/// BDG-2 (ADR-0002): limite mensal recorrente de gasto numa categoria, num escopo PF/PJ, a partir
/// de um mes de vigencia. Dado escrito pelo usuario: fica no <c>expense_reporting</c>, mas fora da
/// projecao (reconstruir a projecao nao apaga orcamentos).
/// </summary>
public sealed class Budget
{
    public const int CategoryMaxLength = 60;
    public const string DefaultCurrency = "BRL";

    private Budget()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Categoria como o usuario digitou (sem espacos nas pontas).</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>Chave normalizada (<see cref="Budgets.CategoryKey"/>) usada na unicidade e no consumo.</summary>
    public string CategoryKey { get; private set; } = string.Empty;

    public BudgetScope Scope { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    /// <summary>Primeiro dia do mes em que o orcamento passa a valer.</summary>
    public DateOnly StartMonth { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Budget> Create(
        Guid ownerUserId,
        string? category,
        BudgetScope scope,
        decimal amount,
        string? currency,
        DateOnly startMonth,
        DateTimeOffset now)
    {
        var trimmed = category?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > CategoryMaxLength)
        {
            return Result.Failure<Budget>(Error.Validation(
                "budget.category_invalid", $"Categoria obrigatoria, com no maximo {CategoryMaxLength} caracteres."));
        }

        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<Budget>(Error.Validation(
                "budget.amount_invalid", "Valor deve ser maior que zero, com no maximo 2 casas decimais."));
        }

        var code = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<Budget>(Error.Validation(
                "budget.currency_invalid", "Moeda deve ser um codigo ISO 4217 de 3 letras (ex.: BRL)."));
        }

        if (!Enum.IsDefined(scope))
        {
            return Result.Failure<Budget>(Error.Validation(
                "budget.scope_invalid", "Escopo invalido: use 'Personal' ou 'Business'."));
        }

        return new Budget
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Category = trimmed,
            CategoryKey = Budgets.CategoryKey.Normalize(trimmed),
            Scope = scope,
            Amount = amount,
            Currency = code,
            StartMonth = new DateOnly(startMonth.Year, startMonth.Month, 1),
            CreatedAt = now
        };
    }
}
