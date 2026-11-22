using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Forecast;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Forecast;

/// <summary>Lancamento fora do cartao, lido da projecao para a previsao.</summary>
public sealed record CashMovementRecord(string TransactionType, decimal Amount, DateOnly OccurredOn, string? AccountScope);

public sealed record MonthFlowDto(
    decimal Income, decimal Expense, decimal Invoices, decimal InvoicesPaid, decimal InvoicesRemaining, decimal Result)
{
    public static MonthFlowDto From(MonthFlow flow) =>
        new(flow.Income, flow.Expense, flow.Invoices, flow.InvoicesPaid, flow.InvoicesRemaining, flow.Result);
}

/// <param name="Month">Mes da previsao, <c>yyyy-MM</c>.</param>
public sealed record ForecastMonthDto(string Month, MonthFlowDto Personal, MonthFlowDto Business, MonthFlowDto Total);

/// <param name="Notice">Lembrete para a tela: a previsao so conhece o que ja esta lancado.</param>
public sealed record CashForecastDto(string Currency, string Notice, IReadOnlyList<ForecastMonthDto> Months);

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

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), options.TimeZone).DateTime);
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = first.AddMonths(months).AddDays(-1);

        var movements = (await projection.GetCashMovementsAsync(ownerUserId, first, last, cancellationToken))
            .Select(m => new CashMovement(m.AccountScope, m.TransactionType == "Income", m.Amount, m.OccurredOn));

        var invoices = new List<ForecastInvoice>();
        foreach (var (card, scope) in await cards.ListWithScopesAsync(cancellationToken))
        {
            var records = await projection.GetCardTransactionsAsync(ownerUserId, card.Id, cancellationToken);
            var payments = await projection.GetCardPaymentsAsync(ownerUserId, card.Id, cancellationToken);
            invoices.AddRange(InvoiceCalculator
                .Calculate(
                    new CardCycle(card.ClosingDay, card.DueDay),
                    GetCardInvoicesHandler.ChargesOf(records),
                    payments.Select(p => new InvoicePayment(p.InvoiceDueMonth, p.Amount)),
                    today)
                .Where(i => i.Period.DueMonth >= first && i.Period.DueMonth <= last)
                .Select(i => new ForecastInvoice(scope, i.Period.DueMonth, i.Total, i.Paid, i.Remaining)));
        }

        var forecast = CashForecastCalculator.Calculate(first, months, movements, invoices);
        return new CashForecastDto(
            "BRL",
            Notice,
            forecast
                .Select(m => new ForecastMonthDto(
                    m.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    MonthFlowDto.From(m.Personal),
                    MonthFlowDto.From(m.Business),
                    MonthFlowDto.From(m.Total)))
                .ToList());
    }
}
