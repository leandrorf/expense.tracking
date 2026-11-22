namespace Expense.Tracking.Reporting.Domain.Forecast;

/// <summary>INV-13: saldo informado de uma conta no fim do dia <see cref="On"/>, com o escopo dela.</summary>
public sealed record AccountOpeningBalance(Guid AccountId, string? Scope, decimal Amount, DateOnly On);

/// <summary>Lancamento fora do cartao de uma conta (entra pela data).</summary>
public sealed record AccountCashMovement(Guid AccountId, bool IsIncome, decimal Amount, DateOnly OccurredOn);

/// <summary>Pagamento de fatura: o dinheiro sai da conta de origem na data do pagamento.</summary>
public sealed record AccountInvoicePayment(Guid AccountId, decimal Amount, DateOnly PaidOn);

/// <summary>O que falta pagar de uma fatura, na conta do cartao, pelo mes de vencimento (dia 1).</summary>
public sealed record AccountInvoiceDue(Guid AccountId, DateOnly DueMonth, decimal Remaining);

/// <summary>Saldo projetado no fim de um mes; nulo quando nenhuma conta do escopo tem saldo informado.</summary>
public sealed record MonthBalance(DateOnly Month, decimal? Personal, decimal? Business, decimal? Total);

/// <summary>
/// INV-13: saldo projetado no fim de cada mes da previsao. So entram as contas com saldo informado.
/// Para cada uma: o saldo do dia informado, mais as entradas e menos as saidas fora do cartao
/// <b>depois</b> desse dia, menos os pagamentos de fatura feitos dela depois desse dia, menos o que
/// ainda falta pagar das faturas dos cartoes dela que vencem do primeiro mes da previsao ate o mes.
/// Fatura vencida antes da previsao nao entra (sem rotativo, ADR-0005, decisao 7). Pagamento e
/// "falta pagar" nao se sobrepoem: o que ja foi pago sai do "falta pagar".
/// </summary>
public static class ProjectedBalanceCalculator
{
    public static IReadOnlyList<MonthBalance> Calculate(
        DateOnly firstMonth,
        int months,
        IEnumerable<AccountOpeningBalance> openings,
        IEnumerable<AccountCashMovement> movements,
        IEnumerable<AccountInvoicePayment> payments,
        IEnumerable<AccountInvoiceDue> invoices)
    {
        var start = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var accounts = openings.GroupBy(o => o.AccountId).Select(g => g.First()).ToList();
        var cash = movements.ToLookup(m => m.AccountId);
        var paid = payments.ToLookup(p => p.AccountId);
        var due = invoices.ToLookup(i => i.AccountId);

        return Enumerable.Range(0, months)
            .Select(offset =>
            {
                var month = start.AddMonths(offset);
                var endOfMonth = month.AddMonths(1).AddDays(-1);
                var balances = accounts
                    .Select(a => (a.Scope, Balance: BalanceOf(a, endOfMonth, month, start, cash[a.AccountId], paid[a.AccountId], due[a.AccountId])))
                    .ToList();
                return new MonthBalance(
                    month,
                    SumOrNull(balances.Where(b => b.Scope == ForecastScopes.Personal).Select(b => b.Balance)),
                    SumOrNull(balances.Where(b => b.Scope == ForecastScopes.Business).Select(b => b.Balance)),
                    SumOrNull(balances.Select(b => b.Balance)));
            })
            .ToList();
    }

    private static decimal BalanceOf(
        AccountOpeningBalance account, DateOnly endOfMonth, DateOnly month, DateOnly firstMonth,
        IEnumerable<AccountCashMovement> cash, IEnumerable<AccountInvoicePayment> paid, IEnumerable<AccountInvoiceDue> due)
    {
        var flows = cash
            .Where(m => m.OccurredOn > account.On && m.OccurredOn <= endOfMonth)
            .Sum(m => m.IsIncome ? m.Amount : -m.Amount);
        var payments = paid.Where(p => p.PaidOn > account.On && p.PaidOn <= endOfMonth).Sum(p => p.Amount);
        var remaining = due.Where(i => i.DueMonth >= firstMonth && i.DueMonth <= month).Sum(i => i.Remaining);
        return account.Amount + flows - payments - remaining;
    }

    private static decimal? SumOrNull(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list.Sum();
    }
}
