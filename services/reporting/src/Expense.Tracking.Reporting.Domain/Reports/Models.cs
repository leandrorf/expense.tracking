namespace Expense.Tracking.Reporting.Domain.Reports;

/// <summary>Lancamento consumido do servico transactions (forma minima para consolidar).</summary>
public sealed record TransactionRecord(
    Guid AccountId,
    string Type,
    string PaymentMethod,
    string Category,
    decimal Amount,
    string Currency);

/// <summary>Total agrupado por uma chave (categoria, escopo, meio de pagamento...).</summary>
public sealed record TotalByKey(string Key, decimal Total);

/// <summary>Resultado consolidado de gastos/receitas num periodo.</summary>
public sealed record ReportSummary(
    string Currency,
    decimal TotalExpense,
    decimal TotalIncome,
    decimal Net,
    int Count,
    IReadOnlyList<TotalByKey> ByCategory,
    IReadOnlyList<TotalByKey> ByScope,
    IReadOnlyList<TotalByKey> ByPaymentMethod);
