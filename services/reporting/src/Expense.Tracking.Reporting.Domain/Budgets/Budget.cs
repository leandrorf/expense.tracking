using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Budgets;

/// <summary>Escopo do orcamento: o mesmo enum de escopo de conta do accounts.</summary>
public enum BudgetScope
{
    Personal = 1,
    Business = 2
}

/// <summary>BDG-3: valor do orcamento a partir de um mes, ate a proxima vigencia.</summary>
public sealed class BudgetLimit
{
    private BudgetLimit()
    {
    }

    internal BudgetLimit(DateOnly effectiveFrom, decimal amount)
    {
        EffectiveFrom = effectiveFrom;
        Amount = amount;
    }

    /// <summary>Primeiro dia do mes em que este valor passa a valer.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    public decimal Amount { get; internal set; }
}

/// <summary>
/// BDG-2/BDG-3 (ADR-0002): limite mensal recorrente de gasto numa categoria, num escopo PF/PJ, a
/// partir de um mes de vigencia. O valor muda por vigencias (<see cref="Limits"/>), sem alterar os
/// meses anteriores, e o orcamento pode ser encerrado a partir de um mes. Dado escrito pelo
/// usuario: fica no <c>expense_reporting</c>, mas fora da projecao (reconstruir a projecao nao
/// apaga orcamentos).
/// </summary>
public sealed class Budget
{
    public const int CategoryMaxLength = 60;
    public const string DefaultCurrency = "BRL";

    private readonly List<BudgetLimit> _limits = [];

    private Budget()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Categoria como o usuario digitou (sem espacos nas pontas).</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>Chave normalizada (<see cref="Budgets.CategoryKey"/>) usada na unicidade e no consumo.</summary>
    public string CategoryKey { get; private set; } = string.Empty;

    /// <summary>
    /// Igual a <see cref="CategoryKey"/> enquanto o orcamento esta aberto e nulo depois de
    /// encerrado. Existe para o indice unico do banco garantir um so orcamento aberto por dono,
    /// escopo e categoria (o MySQL aceita varios nulos num indice unico).
    /// </summary>
    public string? OpenCategoryKey { get; private set; }

    public BudgetScope Scope { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    /// <summary>Primeiro dia do mes em que o orcamento passa a valer.</summary>
    public DateOnly StartMonth { get; private set; }

    /// <summary>Primeiro dia do mes em que o orcamento deixa de valer; nulo enquanto aberto.</summary>
    public DateOnly? ClosedFromMonth { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Vigencias de valor, em ordem de mes; a primeira comeca em <see cref="StartMonth"/>.</summary>
    public IReadOnlyList<BudgetLimit> Limits => _limits.OrderBy(l => l.EffectiveFrom).ToList();

    /// <summary>Valor da vigencia mais recente (o que vale daqui para frente).</summary>
    public decimal LatestAmount => Limits[^1].Amount;

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

        if (ValidateAmount(amount) is { } amountError)
        {
            return Result.Failure<Budget>(amountError);
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

        var key = Budgets.CategoryKey.Normalize(trimmed);
        var start = FirstDayOf(startMonth);
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Category = trimmed,
            CategoryKey = key,
            OpenCategoryKey = key,
            Scope = scope,
            Currency = code,
            StartMonth = start,
            CreatedAt = now
        };
        budget._limits.Add(new BudgetLimit(start, amount));
        return budget;
    }

    /// <summary>
    /// Valor do orcamento no mes, ou nulo se ele nao vale naquele mes (antes do inicio ou a partir
    /// do encerramento).
    /// </summary>
    public decimal? LimitFor(DateOnly month)
    {
        var first = FirstDayOf(month);
        if (first < StartMonth || (ClosedFromMonth is { } closed && first >= closed))
        {
            return null;
        }

        return Limits.Last(l => l.EffectiveFrom <= first).Amount;
    }

    /// <summary>
    /// BDG-3: muda o valor a partir de um mes. Os meses anteriores mantem o valor que tinham; as
    /// vigencias que comecavam depois de <paramref name="fromMonth"/> sao substituidas por esta.
    /// </summary>
    public Result ChangeAmount(decimal amount, DateOnly fromMonth)
    {
        if (ValidateAmount(amount) is { } amountError)
        {
            return Result.Failure(amountError);
        }

        var from = FirstDayOf(fromMonth);
        if (from < StartMonth)
        {
            return Result.Failure(Error.Validation(
                "budget.month_before_start", "O mes da alteracao nao pode ser anterior ao inicio do orcamento."));
        }

        if (ClosedFromMonth is { } closed && from >= closed)
        {
            return Result.Failure(Error.Conflict(
                "budget.closed", "O orcamento esta encerrado neste mes."));
        }

        _limits.RemoveAll(l => l.EffectiveFrom > from);
        // Mesma vigencia: altera o valor no lugar (remover e recriar a mesma chave confunde o EF).
        if (_limits.Find(l => l.EffectiveFrom == from) is { } sameMonth)
        {
            sameMonth.Amount = amount;
        }
        else
        {
            _limits.Add(new BudgetLimit(from, amount));
        }

        return Result.Success();
    }

    /// <summary>
    /// BDG-3: encerra o orcamento a partir de um mes (ele deixa de valer nesse mes e nos seguintes
    /// e continua nos anteriores). Libera a categoria para um orcamento novo nesses meses.
    /// </summary>
    public Result Close(DateOnly fromMonth)
    {
        if (ClosedFromMonth is not null)
        {
            return Result.Failure(Error.Conflict("budget.already_closed", "O orcamento ja esta encerrado."));
        }

        var from = FirstDayOf(fromMonth);
        if (from <= StartMonth)
        {
            return Result.Failure(Error.Validation(
                "budget.month_before_start", "O encerramento precisa ser depois do mes de inicio do orcamento."));
        }

        _limits.RemoveAll(l => l.EffectiveFrom >= from);
        ClosedFromMonth = from;
        OpenCategoryKey = null;
        return Result.Success();
    }

    /// <summary>
    /// Se este orcamento vale em algum mes a partir de <paramref name="startMonth"/> (um orcamento
    /// novo com a mesma categoria e escopo, comecando nesse mes, se sobreporia a ele).
    /// </summary>
    public bool OverlapsFrom(DateOnly startMonth) =>
        ClosedFromMonth is not { } closed || closed > FirstDayOf(startMonth);

    private static Error? ValidateAmount(decimal amount) =>
        amount <= 0 || decimal.Round(amount, 2) != amount
            ? Error.Validation("budget.amount_invalid", "Valor deve ser maior que zero, com no maximo 2 casas decimais.")
            : null;

    private static DateOnly FirstDayOf(DateOnly month) => new(month.Year, month.Month, 1);
}
