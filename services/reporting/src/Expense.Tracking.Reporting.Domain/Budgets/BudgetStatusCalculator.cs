using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Domain.Budgets;

/// <summary>Situacao do orcamento no mes (ADR-0002): calculada na leitura, sem notificacao.</summary>
public enum BudgetHealth
{
    /// <summary>Consumido abaixo do limiar de atencao do orcamento (padrao 80%).</summary>
    OnTrack = 1,

    /// <summary>Consumido entre o limiar de atencao e 100% do limite (inclusive).</summary>
    Warning = 2,

    /// <summary>Consumido acima do limite.</summary>
    Exceeded = 3
}

/// <param name="Remaining">Limite menos consumido; negativo quando estourado (o quanto passou).</param>
/// <param name="Percent">Consumido sobre o limite, em %, com 2 casas.</param>
/// <param name="WarningThreshold">Limiar de atencao do orcamento, em % (BDG-10).</param>
public sealed record BudgetMonthStatus(
    Guid BudgetId,
    string Category,
    BudgetScope Scope,
    string Currency,
    decimal Limit,
    decimal Spent,
    decimal Remaining,
    decimal Percent,
    BudgetHealth Health,
    int WarningThreshold);

public sealed record CategorySpending(string Category, decimal Total);

/// <summary>Gastos sem orcamento no mes, num escopo e moeda.</summary>
public sealed record UnbudgetedSpending(
    BudgetScope Scope, string Currency, decimal Total, IReadOnlyList<CategorySpending> Categories);

/// <summary>
/// BDG-4: limite, consumido, restante e situacao de cada orcamento num mes. Funcao pura: o
/// chamador entrega os lancamentos da projecao ja restritos ao dono e ao mes.
/// </summary>
public static class BudgetStatusCalculator
{
    private const string ExpenseType = "Expense";

    /// <summary>
    /// Consumido = soma dos gastos com o mesmo escopo, moeda e categoria normalizada do
    /// orcamento, em qualquer conta e meio de pagamento. Receitas e escopo desconhecido nao contam.
    /// Orcamentos que nao valem no mes ficam de fora.
    /// </summary>
    public static IReadOnlyList<BudgetMonthStatus> Calculate(
        DateOnly month, IEnumerable<Budget> budgets, IEnumerable<TransactionRecord> monthTransactions)
    {
        var spentByKey = CountedExpenses(monthTransactions)
            .GroupBy(t => KeyOf(t))
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
                    HealthFor(ratio, budget.WarningThreshold),
                    budget.WarningThreshold);
            })
            .OrderBy(s => s.Scope)
            .ThenBy(s => CategoryKey.Normalize(s.Category), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// BDG-8: gastos do mes que nao caem em nenhum orcamento vigente (mesma regra do consumido:
    /// escopo, moeda e categoria normalizada), por escopo e moeda, com as categorias que os compoem.
    /// </summary>
    public static IReadOnlyList<UnbudgetedSpending> CalculateUnbudgeted(
        DateOnly month, IEnumerable<Budget> budgets, IEnumerable<TransactionRecord> monthTransactions)
    {
        var budgeted = budgets
            .Where(b => b.LimitFor(month) is not null)
            .Select(b => (Scope: b.Scope.ToString(), b.Currency, Category: b.CategoryKey))
            .ToHashSet();

        return CountedExpenses(monthTransactions)
            .Where(t => !budgeted.Contains(KeyOf(t)))
            .GroupBy(t => (Scope: t.AccountScope!, t.Currency))
            .Select(g => new UnbudgetedSpending(
                Enum.Parse<BudgetScope>(g.Key.Scope),
                g.Key.Currency,
                g.Sum(t => t.Amount),
                g.GroupBy(t => CategoryKey.Normalize(t.Category))
                    .Select(c => new CategorySpending(DisplayName(c), c.Sum(t => t.Amount)))
                    .OrderByDescending(c => c.Total)
                    .ThenBy(c => c.Category, StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(u => u.Scope)
            .ThenBy(u => u.Currency, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Gastos que entram no acompanhamento: receitas e escopo desconhecido ficam de fora.</summary>
    private static IEnumerable<TransactionRecord> CountedExpenses(IEnumerable<TransactionRecord> transactions) =>
        transactions.Where(t => t.Type == ExpenseType && t.AccountScope is not null && Enum.TryParse<BudgetScope>(t.AccountScope, out _));

    private static (string Scope, string Currency, string Category) KeyOf(TransactionRecord t) =>
        (t.AccountScope!, t.Currency, CategoryKey.Normalize(t.Category));

    /// <summary>Variantes da mesma categoria ("alimentação ", "Alimentação") viram uma so: a mais usada.</summary>
    private static string DisplayName(IEnumerable<TransactionRecord> sameCategory) =>
        CategorySuggestions.PreferredSpelling(sameCategory.Select(t => new CategoryUsage(t.Category, 1)));

    private static BudgetHealth HealthFor(decimal percent, int warningThreshold) => percent switch
    {
        > 100m => BudgetHealth.Exceeded,
        _ when percent >= warningThreshold => BudgetHealth.Warning,
        _ => BudgetHealth.OnTrack
    };
}
