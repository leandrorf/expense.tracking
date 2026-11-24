using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <param name="Kind"><c>InvoiceDue</c>, <c>InvoiceOverdue</c>, <c>RecurrenceDue</c>, <c>RecurrencesToLaunch</c> ou <c>Budget</c>.</param>
/// <param name="Urgency"><c>Overdue</c>, <c>Today</c> ou <c>Soon</c>.</param>
/// <param name="CardId">Fatura: o cartao (atalho para a aba Cartoes).</param>
/// <param name="DueMonth">Fatura: o mes de vencimento, <c>yyyy-MM</c>.</param>
/// <param name="RecurrenceId">Ocorrencia: a regra (atalho para a aba Recorrencias).</param>
/// <param name="Period">Ocorrencia: o periodo, <c>yyyy-MM</c> ou <c>yyyy</c>.</param>
/// <param name="Count">"A lancar": quantas ocorrencias.</param>
public sealed record ReminderDto(
    string Kind,
    string Key,
    string Urgency,
    DateOnly DueOn,
    decimal Amount,
    string Title,
    Guid? CardId,
    string? DueMonth,
    Guid? RecurrenceId,
    string? Period,
    int Count)
{
    public static ReminderDto From(Reminder r) => new(
        r.Kind.ToString(), r.Key, r.Urgency.ToString(), r.DueOn, r.Amount, r.Title, r.CardId, r.DueMonth, r.RecurrenceId,
        r.Period, r.Count);
}

/// <param name="Overdue">Quantos avisos sao de algo ja vencido (o sino os destaca).</param>
/// <param name="LeadDays">Antecedencia usada (dias corridos).</param>
public sealed record RemindersDto(string Currency, int LeadDays, int Overdue, IReadOnlyList<ReminderDto> Items);

/// <summary>
/// NTF-2 (ADR-0009, decisoes 2 e 3): avisos do usuario, calculados a cada consulta com as faturas de
/// todos os cartoes dele (ciclo lido no accounts com o token dele) e as recorrencias projetadas.
/// </summary>
public sealed class GetRemindersHandler(
    ICardDirectory cards, IProjectionQueries projection, IReminderStore store, InvoiceOptions options, TimeProvider timeProvider)
{
    public async Task<RemindersDto> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var today = CardInvoiceSource.Today(timeProvider, options);
        const int leadDays = ReminderCalculator.DefaultLeadDays;

        var invoices = new List<ReminderInvoice>();
        foreach (var (card, _) in await cards.ListWithScopesAsync(cancellationToken))
        {
            var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
            invoices.AddRange(loaded.Invoices.Select(i => new ReminderInvoice(card.Id, card.Label, card.LastFourDigits, i)));
        }

        var recurrences = (await projection.GetRecurrencesAsync(ownerUserId, cancellationToken))
            .Select(r => new ReminderRecurrence(r.Id, r.Description, r.TransactionType != "Income", r.Amount, r.Frequency,
                r.StartsOn, r.EndsOn, r.ClosedPeriods));

        // NTF-5: o dispensado some ate a chave dele mudar (decisao 5).
        var dismissed = await store.ListDismissedKeysAsync(ownerUserId, cancellationToken);
        var reminders = ReminderCalculator.Calculate(today, leadDays, invoices, recurrences)
            .Where(r => !dismissed.Contains(r.Key))
            .ToList();
        return new RemindersDto(
            "BRL",
            leadDays,
            reminders.Count(r => r.Urgency == ReminderUrgency.Overdue),
            reminders.Select(ReminderDto.From).ToList());
    }
}
