using System.Globalization;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Forecast;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Domain.Reminders;

/// <summary>NTF (ADR-0009, decisao 3): de que e o aviso.</summary>
public enum ReminderKind
{
    InvoiceDue = 1,
    InvoiceOverdue = 2,
    RecurrenceDue = 3,
    RecurrencesToLaunch = 4,
    Budget = 5,
}

/// <summary>Urgencia do aviso, da maior para a menor: vencido, vence hoje, vence em breve.</summary>
public enum ReminderUrgency
{
    Overdue = 0,
    Today = 1,
    Soon = 2,
}

/// <summary>Fatura de um cartao, com o que o aviso mostra do cartao.</summary>
public sealed record ReminderInvoice(Guid CardId, string CardLabel, string LastFourDigits, Invoice Invoice);

/// <summary>Regra de recorrencia com os periodos ja fechados (lancados ou pulados).</summary>
public sealed record ReminderRecurrence(
    Guid RecurrenceId, string Description, bool IsExpense, decimal Amount, string Frequency, DateOnly StartsOn,
    DateOnly? EndsOn, IReadOnlySet<string> ClosedPeriods, IReadOnlyList<RecurrenceAmountFrom>? Amounts = null)
{
    /// <summary>RJT-3: valor estimado do periodo pelas vigencias.</summary>
    public decimal AmountFor(string period) => RecurrenceAmounts.For(Amounts ?? [], Amount, period);
}

/// <summary>
/// Um aviso. <see cref="Key"/> e estavel por item e estado (para dispensar, ADR-0009, decisao 5).
/// <see cref="DueOn"/> e a data que vence (no "a lancar", a mais antiga pendente); <see cref="Count"/>
/// so no "a lancar"; <see cref="Limit"/> so no orcamento (o <see cref="Amount"/> e o consumido). O destino do
/// atalho vem nos ids: cartao e mes da fatura, ou regra e periodo.
/// </summary>
public sealed record Reminder(
    ReminderKind Kind,
    string Key,
    ReminderUrgency Urgency,
    DateOnly DueOn,
    decimal Amount,
    string Title,
    Guid? CardId = null,
    string? DueMonth = null,
    Guid? RecurrenceId = null,
    string? Period = null,
    int Count = 0,
    decimal Limit = 0m);

/// <summary>
/// NTF-2 (ADR-0009, decisao 3): o que o usuario precisa resolver. Calculado a cada consulta e nunca
/// guardado: resolver (pagar, lancar, pular) tira o aviso. Ordem: urgencia, depois data.
/// </summary>
public static class ReminderCalculator
{
    public const int DefaultLeadDays = 3;
    public const int MaxLeadDays = 15;

    /// <summary>Fatura vencida deixa de avisar depois disso (o usuario ja sabe, ou ja resolveu fora).</summary>
    public const int OverdueWindowDays = 30;

    /// <summary>Mesma janela do "a lancar" do transactions (REC-3).</summary>
    public const int ToLaunchWindowDays = 60;

    public static IReadOnlyList<Reminder> Calculate(
        DateOnly today, int leadDays, IEnumerable<ReminderInvoice> invoices, IEnumerable<ReminderRecurrence> recurrences,
        bool includeInvoices = true, bool includeRecurrences = true, IEnumerable<BudgetMonthStatus>? budgets = null)
    {
        var horizon = today.AddDays(leadDays);
        var reminders = new List<Reminder>();

        if (includeInvoices)
        {
            foreach (var (cardId, label, lastFour, invoice) in invoices)
            {
                var dueMonth = invoice.Period.DueMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var dueOn = invoice.Period.DueDate;
                var title = $"Fatura {label} •••• {lastFour}";
                if (invoice.Remaining <= 0)
                {
                    continue;
                }

                if (invoice.Status == InvoiceStatus.Overdue && today.DayNumber - dueOn.DayNumber <= OverdueWindowDays)
                {
                    reminders.Add(new Reminder(ReminderKind.InvoiceOverdue, $"invoice:{cardId}:{dueMonth}:overdue",
                        ReminderUrgency.Overdue, dueOn, invoice.Remaining, title, CardId: cardId, DueMonth: dueMonth));
                }
                else if (invoice.Status is InvoiceStatus.Closed or InvoiceStatus.PartiallyPaid && dueOn >= today && dueOn <= horizon)
                {
                    reminders.Add(new Reminder(ReminderKind.InvoiceDue, $"invoice:{cardId}:{dueMonth}:due",
                        dueOn == today ? ReminderUrgency.Today : ReminderUrgency.Soon, dueOn, invoice.Remaining, title,
                        CardId: cardId, DueMonth: dueMonth));
                }
            }
        }

        if (includeRecurrences)
        {
            var toLaunch = new List<DateOnly>();
            foreach (var rule in recurrences)
            {
                foreach (var occurrence in RecurrenceSchedule
                             .Occurrences(rule.Frequency, rule.StartsOn, rule.EndsOn, today.AddDays(-ToLaunchWindowDays), horizon)
                             .Where(o => !rule.ClosedPeriods.Contains(o.Period)))
                {
                    if (occurrence.ScheduledOn < today)
                    {
                        toLaunch.Add(occurrence.ScheduledOn);
                    }
                    else if (rule.IsExpense)
                    {
                        reminders.Add(new Reminder(ReminderKind.RecurrenceDue, $"recurrence:{rule.RecurrenceId}:{occurrence.Period}",
                            occurrence.ScheduledOn == today ? ReminderUrgency.Today : ReminderUrgency.Soon, occurrence.ScheduledOn,
                            rule.AmountFor(occurrence.Period), rule.Description, RecurrenceId: rule.RecurrenceId, Period: occurrence.Period));
                    }
                }
            }

            if (toLaunch.Count > 0)
            {
                // A chave muda quando entra pendencia nova (a mais recente), entao a dispensa nao a esconde.
                var latest = toLaunch.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                reminders.Add(new Reminder(ReminderKind.RecurrencesToLaunch, $"to-launch:{latest}", ReminderUrgency.Overdue,
                    toLaunch.Min(), 0m, toLaunch.Count == 1 ? "1 recorrência a lançar" : $"{toLaunch.Count} recorrências a lançar",
                    Count: toLaunch.Count));
            }
        }

        // NTF-9: orcamento do mes em Atencao (em breve) ou Estourado (urgente). A chave leva a situacao:
        // dispensar o "atencao" nao esconde o "estourado".
        var month = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var endOfMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
        foreach (var budget in (budgets ?? []).Where(b => b.Health is BudgetHealth.Warning or BudgetHealth.Exceeded))
        {
            var exceeded = budget.Health == BudgetHealth.Exceeded;
            var scope = budget.Scope == BudgetScope.Business ? "PJ" : "PF";
            reminders.Add(new Reminder(ReminderKind.Budget, $"budget:{budget.BudgetId}:{month}:{(exceeded ? "exceeded" : "warning")}",
                exceeded ? ReminderUrgency.Overdue : ReminderUrgency.Soon, endOfMonth, budget.Spent,
                $"Orçamento {budget.Category} ({scope})", Limit: budget.Limit));
        }

        return reminders.OrderBy(r => r.Urgency).ThenBy(r => r.DueOn).ThenBy(r => r.Title, StringComparer.Ordinal).ToList();
    }
}
