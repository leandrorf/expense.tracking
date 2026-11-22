using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.AccountingExport;

/// <summary>
/// CTB-3 (ADR-0006, decisoes 3 e 6): <c>resumo.csv</c>, por moeda e categoria, com entradas,
/// saidas (positivas) e saldo, e uma linha de total por moeda. Moedas nunca se misturam. A
/// categoria agrupa como foi digitada, como o Resumo da SPA; a compra parcelada conta inteira.
/// </summary>
public static class AccountingSummarySheet
{
    public const string FileName = "resumo.csv";

    public const string TotalLabel = "Total";

    private static readonly string[] Header = ["Moeda", "Categoria", "Conta contábil", "Entradas", "Saídas", "Saldo"];

    public static string Write(IEnumerable<AccountingEntry> entries)
    {
        var csv = new System.Text.StringBuilder(AccountingCsv.Line(Header));
        foreach (var currency in entries.GroupBy(e => e.Currency).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            foreach (var category in currency.GroupBy(e => e.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var ledger = category.Select(e => e.LedgerAccount).FirstOrDefault(l => !string.IsNullOrEmpty(l));
                csv.Append(Row(currency.Key, AccountingCsv.Text(category.Key), AccountingCsv.Text(ledger), category));
            }

            csv.Append(Row(currency.Key, TotalLabel, string.Empty, currency));
        }

        return csv.ToString();
    }

    private static string Row(string currency, string category, string ledger, IEnumerable<AccountingEntry> entries)
    {
        var income = entries.Where(e => e.Type == TransactionType.Income).Sum(e => e.Amount);
        var expense = entries.Where(e => e.Type == TransactionType.Expense).Sum(e => e.Amount);
        return AccountingCsv.Line(
        [
            currency, category, ledger,
            AccountingCsv.Amount(income), AccountingCsv.Amount(expense), AccountingCsv.Amount(income - expense),
        ]);
    }
}
