using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Domain.Budgets;

/// <summary>Situacao do orcamento no mes (ADR-0002): calculada na leitura, sem notificacao.</summary>
public enum BudgetHealth
{
    /// <summary>Consumido abaixo de 80% do limite.</summary>
    OnTrack = 1,

    /// <summary>Consumido entre 80% e 100% do limite (inclusive).</summary>
    Warning = 2,

    /// <summary>Consumido acima do limite.</summary>
    Exceeded = 3
}

/// <param name="Remaining">Limite menos consumido; negativo quando estourado (o quanto passou).</param>
/// <param name="Percent">Consumido sobre o limite, em %, com 2 casas.</param>
public sealed record BudgetMonthStatus(
    Guid BudgetId,
    string Category,
    BudgetScope Scope,
    string Currency,
    decimal Limit,
    decimal Spent,
    decimal Remaining,
    decimal Percent,
    BudgetHealth Health);

/// <summary>
/// BDG-4: limite, consumido, restante e situacao de cada orcamento num mes. Funcao pura: o
/// chamador entrega os lancamentos da projecao ja restritos ao dono e ao mes.
/// </summary>
public static class BudgetStatusCalculator
{
    /// <summary>A partir deste percentual o orcamento entra em atencao.</summary>
    public const decimal WarningThreshold = 80m;

    private const string ExpenseType = "Expense";

    /// <summary>
    /// Consumido = soma dos gastos com o mesmo escopo, moeda e categoria normalizada do
    /// orcamento, em qualquer conta e meio de pagamento. Receitas e escopo desconhecido nao contam.
    /// Orcamentos que nao valem no mes ficam de fora.
    /// </summary>
    public static IReadOnlyList<BudgetMonthStatus> Calculate(
        DateOnly month, IEnumerable<Budget> budgets, IEnumerable<TransactionRecord> monthTransactions)
    {
        var spentByKey = monthTransactions
            .Where(t => t.Type == ExpenseType && t.AccountScope is not null)
            .GroupBy(t => (Scope: t.AccountScope!, t.Currency, Category: CategoryKey.Normalize(t.Category)))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        return budgets
            .Select(b => (Budget: b, Limit: b.LimitFor(month)))
            .Where(x => x.Limit is not null)
            .Select(x =>
            {
                var budget = x.Budget;
                var limit = x.Limit!.Value;
                var spent = spentByKey.GetValueOrDefault((budget.Scope.ToString(), budget.Currency, budget.CategoryKey));
                var ratio = spent / limit * 100m;
                return new BudgetMonthStatus(
                    budget.Id,
                    budget.Category,
                    budget.Scope,
                    budget.Currency,
                    limit,
                    spent,
                    limit - spent,
                    decimal.Round(ratio, 2, MidpointRounding.AwayFromZero),
                    HealthFor(ratio));
            })
            .OrderBy(s => s.Scope)
            .ThenBy(s => CategoryKey.Normalize(s.Category), StringComparer.Ordinal)
            .ToList();
    }

    private static BudgetHealth HealthFor(decimal percent) => percent switch
    {
        > 100m => BudgetHealth.Exceeded,
        >= WarningThreshold => BudgetHealth.Warning,
        _ => BudgetHealth.OnTrack
    };
}
