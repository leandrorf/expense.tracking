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
/// <param name="Limit">Orcamento: o limite do mes (o <c>Amount</c> e o consumido).</param>
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
    int Count,
    decimal Limit)
{
    public static ReminderDto From(Reminder r) => new(
        r.Kind.ToString(), r.Key, r.Urgency.ToString(), r.DueOn, r.Amount, r.Title, r.CardId, r.DueMonth, r.RecurrenceId,
        r.Period, r.Count, r.Limit);
}

/// <param name="Overdue">Quantos avisos sao de algo ja vencido (o sino os destaca).</param>
/// <param name="LeadDays">Antecedencia usada (dias corridos).</param>
public sealed record RemindersDto(string Currency, int LeadDays, int Overdue, IReadOnlyList<ReminderDto> Items);

/// <summary>
/// NTF-2 (ADR-0009, decisoes 2 e 3): avisos do usuario, calculados a cada consulta com as faturas de
/// todos os cartoes dele (ciclo lido no accounts com o token dele) e as recorrencias projetadas, nas
/// preferencias dele (NTF-6) e sem os dispensados (NTF-5). O calculo e o mesmo do e-mail (ReminderComposer).
/// </summary>
public sealed class GetRemindersHandler(
    ICardDirectory cards, ReminderComposer composer, IReminderStore store, InvoiceOptions options, TimeProvider timeProvider)
{
    public async Task<RemindersDto> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var today = CardInvoiceSource.Today(timeProvider, options);
        var settings = await store.FindSettingsAsync(ownerUserId, cancellationToken) ?? ReminderSettings.Default(ownerUserId);
        IReadOnlyList<CardInfo> userCards = settings.Invoices
            ? (await cards.ListWithScopesAsync(cancellationToken)).Select(c => c.Card).ToList()
            : [];

        var reminders = await composer.ComposeAsync(ownerUserId, userCards, settings, today, cancellationToken);
        return new RemindersDto(
            "BRL",
            settings.LeadDays,
            reminders.Count(r => r.Urgency == ReminderUrgency.Overdue),
            reminders.Select(ReminderDto.From).ToList());
    }
}
