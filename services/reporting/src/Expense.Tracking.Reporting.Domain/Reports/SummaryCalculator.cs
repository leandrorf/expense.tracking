namespace Expense.Tracking.Reporting.Domain.Reports;

/// <summary>
/// Calculo puro da consolidacao. Recebe os lancamentos e um mapa conta->escopo (PF/PJ)
/// e produz os totais. Sem I/O — totalmente testavel.
/// </summary>
public static class SummaryCalculator
{
    public const string Expense = "Expense";
    public const string Income = "Income";
    private const string DefaultCurrency = "BRL";

    public static ReportSummary Calculate(
        IReadOnlyList<TransactionRecord> records,
        IReadOnlyDictionary<Guid, string> scopeByAccount)
    {
        var currency = records
            .Select(r => r.Currency)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? DefaultCurrency;

        var expenses = records.Where(r => IsExpense(r.Type)).ToArray();

        var totalExpense = expenses.Sum(r => r.Amount);
        var totalIncome = records.Where(r => IsIncome(r.Type)).Sum(r => r.Amount);

        return new ReportSummary(
            Currency: currency,
            TotalExpense: totalExpense,
            TotalIncome: totalIncome,
            Net: totalIncome - totalExpense,
            Count: records.Count,
            ByCategory: GroupTotals(expenses, r => r.Category),
            ByScope: GroupTotals(expenses, r => scopeByAccount.GetValueOrDefault(r.AccountId, "Unknown")),
            ByPaymentMethod: GroupTotals(expenses, r => r.PaymentMethod));
    }

    private static bool IsExpense(string type) => string.Equals(type, Expense, StringComparison.OrdinalIgnoreCase);

    private static bool IsIncome(string type) => string.Equals(type, Income, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<TotalByKey> GroupTotals(
        IEnumerable<TransactionRecord> records,
        Func<TransactionRecord, string> keySelector)
    {
        return records
            .GroupBy(keySelector)
            .Select(g => new TotalByKey(g.Key, g.Sum(r => r.Amount)))
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
