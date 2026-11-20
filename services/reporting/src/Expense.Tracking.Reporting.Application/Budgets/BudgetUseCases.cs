using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Application.Budgets;

public sealed record BudgetDto(
    Guid Id,
    string Category,
    string Scope,
    decimal Amount,
    string Currency,
    string StartMonth)
{
    public static BudgetDto From(Budget budget) => new(
        budget.Id,
        budget.Category,
        budget.Scope.ToString(),
        budget.Amount,
        budget.Currency,
        budget.StartMonth.ToString("yyyy-MM"));
}

/// <summary>BDG-2: orcamentos do usuario no banco do reporting (ADR-0002).</summary>
public interface IBudgetRepository
{
    Task<bool> ExistsAsync(Guid ownerUserId, BudgetScope scope, string categoryKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<Budget>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Grava o orcamento; devolve <c>false</c> se o indice unico acusar duplicata (corrida).</summary>
    Task<bool> TryAddAsync(Budget budget, CancellationToken cancellationToken);
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
        "budget.duplicate", "Ja existe um orcamento para esta categoria neste escopo.");

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
        if (await budgets.ExistsAsync(budget.OwnerUserId, budget.Scope, budget.CategoryKey, cancellationToken)
            || !await budgets.TryAddAsync(budget, cancellationToken))
        {
            return Result.Failure<BudgetDto>(Duplicate);
        }

        return BudgetDto.From(budget);
    }
}

public sealed class ListBudgetsHandler(IBudgetRepository budgets)
{
    public async Task<IReadOnlyList<BudgetDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var list = await budgets.ListAsync(ownerUserId, cancellationToken);
        return list
            .OrderBy(b => b.Scope)
            .ThenBy(b => b.CategoryKey, StringComparer.Ordinal)
            .Select(BudgetDto.From)
            .ToList();
    }
}
