using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Forecast;

namespace Expense.Tracking.Reporting.Application.Forecast;

/// <summary>Lancamento fora do cartao, lido da projecao para a previsao.</summary>
public sealed record CashMovementRecord(
    string TransactionType, decimal Amount, DateOnly OccurredOn, string? AccountScope, Guid AccountId);

/// <summary>INV-13: conta como o accounts a devolve ao dono (so o que a previsao usa).</summary>
public sealed record AccountInfo(Guid Id, string? Scope, bool IsActive, decimal? OpeningBalance, DateOnly? OpeningBalanceOn);

/// <param name="Balance">INV-13: saldo projetado no fim do mes; nulo sem conta com saldo informado no escopo.</param>
public sealed record MonthFlowDto(
    decimal Income, decimal Expense, decimal Invoices, decimal InvoicesPaid, decimal InvoicesRemaining, decimal Result,
    decimal? Balance)
{
    public static MonthFlowDto From(MonthFlow flow, decimal? balance) =>
        new(flow.Income, flow.Expense, flow.Invoices, flow.InvoicesPaid, flow.InvoicesRemaining, flow.Result, balance);
}

/// <param name="Month">Mes da previsao, <c>yyyy-MM</c>.</param>
public sealed record ForecastMonthDto(string Month, MonthFlowDto Personal, MonthFlowDto Business, MonthFlowDto Total);

/// <param name="Notice">Lembrete para a tela: a previsao so conhece o que ja esta lancado.</param>
/// <param name="AccountsWithoutBalance">INV-13: contas ativas sem saldo informado (ficam fora do saldo projetado).</param>
public sealed record CashForecastDto(
    string Currency, string Notice, IReadOnlyList<ForecastMonthDto> Months, int AccountsWithoutBalance);

/// <summary>
/// INV-8 (ADR-0005, decisao 8): previsao de caixa do mes atual ate <c>months</c> meses. Lancamentos
/// fora do cartao vem da projecao; as faturas sao as da INV-4 de todos os cartoes do usuario (ativos
/// ou nao: um cartao desativado ainda pode ter parcelas a vencer), no escopo da conta do cartao.
/// </summary>
public sealed class GetCashForecastHandler(
    ICardDirectory cards, IProjectionQueries projection, InvoiceOptions options, TimeProvider timeProvider)
{
    public const string Notice = "A previsão considera só o que já está lançado: lançamentos com data futura e faturas dos cartões.";

    public static readonly Error MonthsInvalid = Error.Validation(
        "forecast.months_invalid", $"Informe de 1 a {CashForecastCalculator.MaxMonths} meses.");

    public async Task<Result<CashForecastDto>> HandleAsync(Guid ownerUserId, int months, CancellationToken cancellationToken)
    {
        if (months is < 1 or > CashForecastCalculator.MaxMonths)
        {
            return Result.Failure<CashForecastDto>(MonthsInvalid);
        }

        var today = CardInvoiceSource.Today(timeProvider, options);
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = first.AddMonths(months).AddDays(-1);

        // INV-13: o saldo projetado precisa do que veio depois do saldo informado mais antigo, que
        // pode ser anterior a previsao; o fluxo do mes continua so com o que cai na janela.
        var accounts = await cards.ListAccountsAsync(cancellationToken);
        var openings = accounts
            .Where(a => a.OpeningBalance is not null && a.OpeningBalanceOn is not null)
            .Select(a => new AccountOpeningBalance(a.Id, a.Scope, a.OpeningBalance!.Value, a.OpeningBalanceOn!.Value))
            .ToList();
        var from = openings.Count == 0 ? first : Min(first, openings.Min(o => o.On).AddDays(1));

        var cash = await projection.GetCashMovementsAsync(ownerUserId, from, last, cancellationToken);
        var movements = cash
            .Where(m => m.OccurredOn >= first)
            .Select(m => new CashMovement(m.AccountScope, m.TransactionType == "Income", m.Amount, m.OccurredOn));

        var invoices = new List<ForecastInvoice>();
        var invoicesDue = new List<AccountInvoiceDue>();
        var payments = new List<AccountInvoicePayment>();
        foreach (var (card, scope) in await cards.ListWithScopesAsync(cancellationToken))
        {
            var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
            var inWindow = loaded.Invoices.Where(i => i.Period.DueMonth >= first && i.Period.DueMonth <= last).ToList();
            invoices.AddRange(inWindow.Select(i => new ForecastInvoice(scope, i.Period.DueMonth, i.Total, i.Paid, i.Remaining)));
            invoicesDue.AddRange(inWindow.Select(i => new AccountInvoiceDue(card.AccountId, i.Period.DueMonth, i.Remaining)));
            payments.AddRange(loaded.Payments.Select(p => new AccountInvoicePayment(p.AccountId, p.Amount, p.PaidOn)));
        }

        var forecast = CashForecastCalculator.Calculate(first, months, movements, invoices);
        var balances = ProjectedBalanceCalculator.Calculate(
            first, months, openings,
            cash.Select(m => new AccountCashMovement(m.AccountId, m.TransactionType == "Income", m.Amount, m.OccurredOn)),
            payments, invoicesDue);

        return new CashForecastDto(
            "BRL",
            Notice,
            forecast
                .Zip(balances, (m, b) => new ForecastMonthDto(
                    m.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    MonthFlowDto.From(m.Personal, b.Personal),
                    MonthFlowDto.From(m.Business, b.Business),
                    MonthFlowDto.From(m.Total, b.Total)))
                .ToList(),
            accounts.Count(a => a.IsActive && a.OpeningBalance is null));
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
