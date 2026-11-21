using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Application.Budgets;

public sealed record BudgetLimitDto(string FromMonth, decimal Amount);

/// <param name="Amount">
/// Valor no mes consultado (<c>?month=</c>) ou, sem mes, o da vigencia mais recente.
/// </param>
/// <param name="ClosedFromMonth">Mes a partir do qual o orcamento nao vale mais; nulo se aberto.</param>
public sealed record BudgetDto(
    Guid Id,
    string Category,
    string Scope,
    decimal Amount,
    string Currency,
    string StartMonth,
    string? ClosedFromMonth,
    IReadOnlyList<BudgetLimitDto> Limits)
{
    public static BudgetDto From(Budget budget, decimal? amount = null) => new(
        budget.Id,
        budget.Category,
        budget.Scope.ToString(),
        amount ?? budget.LatestAmount,
        budget.Currency,
        ToMonth(budget.StartMonth),
        budget.ClosedFromMonth is { } closed ? ToMonth(closed) : null,
        budget.Limits.Select(l => new BudgetLimitDto(ToMonth(l.EffectiveFrom), l.Amount)).ToList());

    private static string ToMonth(DateOnly month) => month.ToString("yyyy-MM");
}

/// <summary>BDG-2/BDG-3: orcamentos do usuario no banco do reporting (ADR-0002).</summary>
public interface IBudgetRepository
{
    /// <summary>
    /// Se ja existe orcamento do dono, no escopo e categoria, que valha em algum mes a partir de
    /// <paramref name="startMonth"/>.
    /// </summary>
    Task<bool> OverlapsAsync(Guid ownerUserId, BudgetScope scope, string categoryKey, DateOnly startMonth, CancellationToken cancellationToken);

    Task<IReadOnlyList<Budget>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Orcamento do dono para alterar; nulo se nao existe ou e de outro usuario.</summary>
    Task<Budget?> FindAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Grava o orcamento; devolve <c>false</c> se o indice unico acusar duplicata (corrida).</summary>
    Task<bool> TryAddAsync(Budget budget, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record CreateBudgetCommand(
    Guid OwnerUserId,
    string? Category,
    BudgetScope Scope,
    decimal Amount,
    string? Currency,
    DateOnly StartMonth);

public sealed class CreateBudgetHandler(IBudgetRepository budgets, TimeProvider clock)
{
    private static readonly Error Duplicate = Error.Conflict(
        "budget.duplicate", "Ja existe um orcamento para esta categoria neste escopo nesses meses.");

    public async Task<Result<BudgetDto>> HandleAsync(CreateBudgetCommand command, CancellationToken cancellationToken)
    {
        var created = Budget.Create(
            command.OwnerUserId, command.Category, command.Scope, command.Amount, command.Currency,
            command.StartMonth, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<BudgetDto>(created.Error);
        }

        var budget = created.Value;
        if (await budgets.OverlapsAsync(budget.OwnerUserId, budget.Scope, budget.CategoryKey, budget.StartMonth, cancellationToken)
            || !await budgets.TryAddAsync(budget, cancellationToken))
        {
            return Result.Failure<BudgetDto>(Duplicate);
        }

        return BudgetDto.From(budget);
    }
}

public sealed class ListBudgetsHandler(IBudgetRepository budgets)
{
    /// <param name="month">Com mes, so os orcamentos que valem nele, com o valor daquele mes.</param>
    public async Task<IReadOnlyList<BudgetDto>> HandleAsync(Guid ownerUserId, DateOnly? month, CancellationToken cancellationToken)
    {
        var list = await budgets.ListAsync(ownerUserId, cancellationToken);
        var ordered = list.OrderBy(b => b.Scope).ThenBy(b => b.CategoryKey, StringComparer.Ordinal).ThenBy(b => b.StartMonth);

        if (month is not { } m)
        {
            return ordered.Select(b => BudgetDto.From(b)).ToList();
        }

        return ordered
            .Select(b => (Budget: b, Limit: b.LimitFor(m)))
            .Where(x => x.Limit is not null)
            .Select(x => BudgetDto.From(x.Budget, x.Limit))
            .ToList();
    }
}

public sealed record ChangeBudgetAmountCommand(Guid Id, Guid OwnerUserId, decimal Amount, DateOnly FromMonth);

public sealed record CloseBudgetCommand(Guid Id, Guid OwnerUserId, DateOnly FromMonth);

/// <summary>BDG-3: alterar o valor a partir de um mes e encerrar. Orcamento alheio = 404.</summary>
public sealed class ChangeBudgetHandler(IBudgetRepository budgets)
{
    private static readonly Error NotFound = Error.NotFound("budget.not_found", "Orcamento nao encontrado.");

    public Task<Result<BudgetDto>> HandleAsync(ChangeBudgetAmountCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.Id, command.OwnerUserId, b => b.ChangeAmount(command.Amount, command.FromMonth), cancellationToken);

    public Task<Result<BudgetDto>> HandleAsync(CloseBudgetCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.Id, command.OwnerUserId, b => b.Close(command.FromMonth), cancellationToken);

    private async Task<Result<BudgetDto>> ApplyAsync(
        Guid id, Guid ownerUserId, Func<Budget, Result> change, CancellationToken cancellationToken)
    {
        var budget = await budgets.FindAsync(id, ownerUserId, cancellationToken);
        if (budget is null)
        {
            return Result.Failure<BudgetDto>(NotFound);
        }

        var result = change(budget);
        if (result.IsFailure)
        {
            return Result.Failure<BudgetDto>(result.Error);
        }

        await budgets.SaveChangesAsync(cancellationToken);
        return BudgetDto.From(budget);
    }
}

public sealed record BudgetStatusDto(
    Guid BudgetId,
    string Category,
    string Scope,
    string Currency,
    decimal Limit,
    decimal Spent,
    decimal Remaining,
    decimal Percent,
    string Status);

public sealed record CategoryTotalDto(string Category, decimal Total);

public sealed record UnbudgetedDto(string Scope, string Currency, decimal Total, IReadOnlyList<CategoryTotalDto> Categories);

/// <param name="Month">Mes consultado (<c>yyyy-MM</c>).</param>
/// <param name="Unbudgeted">BDG-8: gastos sem orcamento no mes, por escopo e moeda.</param>
public sealed record BudgetMonthDto(string Month, IReadOnlyList<BudgetStatusDto> Budgets, IReadOnlyList<UnbudgetedDto> Unbudgeted);

/// <summary>
/// BDG-4: acompanhamento do mes. O consumido sai da projecao (mesma fonte e mesma defasagem do
/// Resumo), entao responde mesmo com o transactions fora do ar.
/// </summary>
public sealed class GetBudgetStatusHandler(IBudgetRepository budgets, IProjectionQueries projection)
{
    public async Task<BudgetMonthDto> HandleAsync(Guid ownerUserId, DateOnly month, CancellationToken cancellationToken)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var list = await budgets.ListAsync(ownerUserId, cancellationToken);
        var transactions = await projection.GetTransactionsAsync(ownerUserId, new ReportFilter(From: first, To: last), cancellationToken);

        var statuses = BudgetStatusCalculator.Calculate(first, list, transactions)
            .Select(s => new BudgetStatusDto(
                s.BudgetId, s.Category, s.Scope.ToString(), s.Currency, s.Limit, s.Spent, s.Remaining, s.Percent, s.Health.ToString()))
            .ToList();
        var unbudgeted = BudgetStatusCalculator.CalculateUnbudgeted(first, list, transactions)
            .Select(u => new UnbudgetedDto(
                u.Scope.ToString(), u.Currency, u.Total,
                u.Categories.Select(c => new CategoryTotalDto(c.Category, c.Total)).ToList()))
            .ToList();
        return new BudgetMonthDto(first.ToString("yyyy-MM"), statuses, unbudgeted);
    }
}

/// <summary>
/// BDG-7: categorias ja usadas pelo dono, para sugerir nos formularios de orcamento e de
/// lancamento. Vem dos lancamentos da projecao e dos orcamentos (um orcamento ainda sem gasto
/// tambem e sugerido no lancamento, o que evita digitar a categoria de outro jeito).
/// </summary>
public sealed class GetCategorySuggestionsHandler(IBudgetRepository budgets, IProjectionQueries projection)
{
    public async Task<IReadOnlyList<string>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var used = await projection.GetCategoryUsageAsync(ownerUserId, cancellationToken);
        var budgeted = await budgets.ListAsync(ownerUserId, cancellationToken);
        return CategorySuggestions.Build(used.Concat(budgeted.Select(b => new CategoryUsage(b.Category, 1))));
    }
}
