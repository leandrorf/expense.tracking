using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Forecast;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Forecast;

/// <summary>Lancamento fora do cartao, lido da projecao para a previsao.</summary>
public sealed record CashMovementRecord(
    string TransactionType, decimal Amount, DateOnly OccurredOn, string? AccountScope, Guid AccountId);

/// <summary>REC-4: regra de recorrencia projetada, com os periodos fechados dela.</summary>
public sealed record RecurrenceRecord(
    Guid Id,
    Guid AccountId,
    string? AccountScope,
    string TransactionType,
    string PaymentMethod,
    Guid? CreditCardId,
    decimal Amount,
    string Frequency,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    IReadOnlySet<string> ClosedPeriods,
    string Description = "",
    IReadOnlyList<RecurrenceAmountFrom>? Amounts = null,
    RecurrenceAdjustmentEstimate? Adjustment = null)
{
    /// <summary>RJT-3: valor estimado de um periodo (sem vigencias, o <see cref="Amount"/>).</summary>
    public decimal AmountFor(string period) => RecurrenceAmounts.For(Amounts ?? [], Amount, period);

    /// <summary>RJT-8: o reajuste estimado a frente, visto de <paramref name="today"/>.</summary>
    public EstimatedAdjustment? EstimateFrom(DateOnly today) =>
        RecurrenceAmounts.Estimate(Amounts ?? [], Amount, Frequency, StartsOn, today, Adjustment);

    /// <summary>RJT-8: valor do periodo na previsao, com o reajuste estimado.</summary>
    public ForecastAmount ForecastAmountFor(string period, EstimatedAdjustment? estimate) =>
        RecurrenceAmounts.ForForecast(Amounts ?? [], Amount, period, estimate);
}

/// <summary>INV-13: conta como o accounts a devolve ao dono (so o que a previsao usa).</summary>
public sealed record AccountInfo(Guid Id, string? Scope, bool IsActive, decimal? OpeningBalance, DateOnly? OpeningBalanceOn);

/// <param name="Balance">INV-13: saldo projetado no fim do mes; nulo sem conta com saldo informado no escopo.</param>
/// <param name="RecurringIncome">REC-4: parte das entradas que vem de recorrencias ainda nao lancadas.</param>
/// <param name="RecurringExpense">REC-4: parte das saidas que vem de recorrencias ainda nao lancadas.</param>
/// <param name="RecurringInvoices">REC-4: parte das faturas que vem de recorrencias no cartao ainda nao lancadas.</param>
/// <param name="EstimatedRecurring">RJT-8: quanto da parte recorrente e reajuste estimado, ainda nao confirmado.</param>
public sealed record MonthFlowDto(
    decimal Income, decimal Expense, decimal Invoices, decimal InvoicesPaid, decimal InvoicesRemaining, decimal Result,
    decimal? Balance, decimal RecurringIncome, decimal RecurringExpense, decimal RecurringInvoices, decimal EstimatedRecurring)
{
    public static MonthFlowDto From(MonthFlow flow, decimal? balance) =>
        new(flow.Income, flow.Expense, flow.Invoices, flow.InvoicesPaid, flow.InvoicesRemaining, flow.Result, balance,
            flow.RecurringIncome, flow.RecurringExpense, flow.RecurringInvoices, flow.EstimatedRecurring);
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
        var scopedCards = await cards.ListWithScopesAsync(cancellationToken);
        foreach (var (card, scope) in scopedCards)
        {
            var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
            var inWindow = loaded.Invoices.Where(i => i.Period.DueMonth >= first && i.Period.DueMonth <= last).ToList();
            invoices.AddRange(inWindow.Select(i => new ForecastInvoice(scope, i.Period.DueMonth, i.Total, i.Paid, i.Remaining)));
            invoicesDue.AddRange(inWindow.Select(i => new AccountInvoiceDue(card.AccountId, i.Period.DueMonth, i.Remaining)));
            payments.AddRange(loaded.Payments.Select(p => new AccountInvoicePayment(p.AccountId, p.Amount, p.PaidOn)));
        }

        // REC-4 (ADR-0008, decisao 9): cada periodo em aberto de hoje em diante entra como um lancamento
        // futuro do valor estimado: fora do cartao pela data, no cartao pela fatura do vencimento.
        var recurring = await RecurringAsync(ownerUserId, today, last, scopedCards, cancellationToken);
        var allMovements = movements.Concat(recurring.Movements).ToList();
        invoices.AddRange(recurring.Invoices.Where(i => i.Invoice.DueMonth >= first && i.Invoice.DueMonth <= last).Select(i => i.Invoice));
        invoicesDue.AddRange(recurring.Invoices
            .Where(i => i.Invoice.DueMonth >= first && i.Invoice.DueMonth <= last)
            .Select(i => new AccountInvoiceDue(i.CardAccountId, i.Invoice.DueMonth, i.Invoice.Remaining)));

        var forecast = CashForecastCalculator.Calculate(first, months, allMovements, invoices);
        var balances = ProjectedBalanceCalculator.Calculate(
            first, months, openings,
            cash.Select(m => new AccountCashMovement(m.AccountId, m.TransactionType == "Income", m.Amount, m.OccurredOn))
                .Concat(recurring.AccountMovements),
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

    private sealed record RecurringInvoice(ForecastInvoice Invoice, Guid CardAccountId);

    private sealed record RecurringFlows(
        IReadOnlyList<CashMovement> Movements, IReadOnlyList<AccountCashMovement> AccountMovements, IReadOnlyList<RecurringInvoice> Invoices);

    /// <summary>
    /// Ocorrencias em aberto (nem lancadas nem puladas) das regras do dono, com data de hoje ate o fim
    /// da janela. A que ja passou fica no "a lancar" do transactions, fora da previsao (decisao 8).
    /// Recorrencia num cartao que o accounts nao devolve ao dono fica de fora.
    /// </summary>
    private async Task<RecurringFlows> RecurringAsync(
        Guid ownerUserId, DateOnly today, DateOnly last, IReadOnlyList<ScopedCard> scopedCards, CancellationToken cancellationToken)
    {
        var movements = new List<CashMovement>();
        var accountMovements = new List<AccountCashMovement>();
        var invoices = new List<RecurringInvoice>();
        var cardsById = scopedCards.ToDictionary(c => c.Card.Id);

        foreach (var rule in await projection.GetRecurrencesAsync(ownerUserId, cancellationToken))
        {
            var isIncome = rule.TransactionType == "Income";
            var onCard = rule.CreditCardId is not null || rule.PaymentMethod == "CreditCard";
            var estimate = rule.EstimateFrom(today);
            foreach (var occurrence in RecurrenceSchedule.Occurrences(rule.Frequency, rule.StartsOn, rule.EndsOn, today, last)
                         .Where(o => !rule.ClosedPeriods.Contains(o.Period)))
            {
                if (!onCard)
                {
                    var (amount, estimatedPart) = rule.ForecastAmountFor(occurrence.Period, estimate);
                    movements.Add(new CashMovement(rule.AccountScope, isIncome, amount, occurrence.ScheduledOn, Recurring: true, estimatedPart));
                    accountMovements.Add(new AccountCashMovement(rule.AccountId, isIncome, amount, occurrence.ScheduledOn));
                    continue;
                }

                if (rule.CreditCardId is not { } cardId || !cardsById.TryGetValue(cardId, out var scoped))
                {
                    continue;
                }

                // A vista, na fatura da regra de corte (ADR-0005, decisao 4); receita no cartao e estorno.
                var dueMonth = new CardCycle(scoped.Card.ClosingDay, scoped.Card.DueDay).DueMonthOf(occurrence.ScheduledOn);
                var (periodAmount, cardEstimatedPart) = rule.ForecastAmountFor(occurrence.Period, estimate);
                var signed = isIncome ? -periodAmount : periodAmount;
                invoices.Add(new RecurringInvoice(
                    new ForecastInvoice(scoped.Scope, dueMonth, signed, 0m, signed, Recurring: true, cardEstimatedPart), scoped.Card.AccountId));
            }
        }

        return new RecurringFlows(movements, accountMovements, invoices);
    }
}
