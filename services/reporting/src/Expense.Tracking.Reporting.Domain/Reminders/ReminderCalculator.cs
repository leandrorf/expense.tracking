using System.Globalization;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Forecast;
using Expense.Tracking.Reporting.Domain.Goals;
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

    /// <summary>RJT-9 (ADR-0010, decisao 10): reajuste anual chegado e ainda nao confirmado.</summary>
    RecurrenceAdjustment = 6,

    /// <summary>MET-9 (ADR-0013, regra 10): a meta do mes atual esta fora.</summary>
    MonthlyGoal = 7,

    /// <summary>MET-9 (ADR-0013, regra 10): objetivo atrasado a 3 meses ou menos do prazo.</summary>
    SavingsObjective = 8,
}

/// <summary>MET-9: a meta do mes atual num escopo, ja avaliada (<see cref="MonthlyGoalStatusCalculator"/>).</summary>
public sealed record MonthlyGoalCheck(GoalScope Scope, MonthlyGoalKind Kind, MonthlyGoalSituation Situation, decimal Target, MonthFigures Figures);

/// <summary>MET-9: um objetivo ativo com o ritmo calculado (<see cref="SavingsPaceCalculator"/>).</summary>
public sealed record SavingsObjectiveCheck(Guid ObjectiveId, string Name, GoalScope Scope, DateOnly? Deadline, SavingsPace Pace);

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
    DateOnly? EndsOn, IReadOnlySet<string> ClosedPeriods, IReadOnlyList<RecurrenceAmountFrom>? Amounts = null,
    RecurrenceAdjustmentEstimate? Adjustment = null)
{
    /// <summary>RJT-3: valor estimado do periodo pelas vigencias.</summary>
    public decimal AmountFor(string period) => RecurrenceAmounts.For(Amounts ?? [], Amount, period);
}

/// <summary>
/// Um aviso. <see cref="Key"/> e estavel por item e estado (para dispensar, ADR-0009, decisao 5).
/// <see cref="DueOn"/> e a data que vence (no "a lancar", a mais antiga pendente); <see cref="Count"/>
/// so no "a lancar"; <see cref="Limit"/> so no orcamento (o <see cref="Amount"/> e o consumido). O destino do
/// atalho vem nos ids: cartao e mes da fatura, ou regra e periodo. No reajuste (RJT-9), <see cref="PreviousAmount"/>
/// e o valor atual, <see cref="Amount"/> o estimado e <see cref="Rate"/>/<see cref="Index"/> a taxa e o indice
/// (taxa nula = aguardando o indice; <see cref="Amount"/> fica o atual).
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
    decimal Limit = 0m,
    decimal? PreviousAmount = null,
    decimal? Rate = null,
    string? Index = null,
    Guid? ObjectiveId = null,
    string? Scope = null);

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

    /// <summary>MET-9: o objetivo atrasado avisa a partir de quantos meses do prazo (contando o atual).</summary>
    public const int ObjectiveWarningMonths = 3;

    public static IReadOnlyList<Reminder> Calculate(
        DateOnly today, int leadDays, IEnumerable<ReminderInvoice> invoices, IEnumerable<ReminderRecurrence> recurrences,
        bool includeInvoices = true, bool includeRecurrences = true, IEnumerable<BudgetMonthStatus>? budgets = null,
        IEnumerable<MonthlyGoalCheck>? monthlyGoals = null, IEnumerable<SavingsObjectiveCheck>? objectives = null)
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

            // RJT-9: reajuste anual chegado e ainda sem vigencia. A chave e por aniversario: confirmar tira o aviso.
            foreach (var rule in recurrences)
            {
                if (rule.Adjustment is not { } adjustment
                    || RecurrenceAmounts.Pending(rule.Amounts ?? [], rule.Amount, rule.Frequency, rule.StartsOn, rule.EndsOn, today,
                        adjustment.AnniversaryMonth) is not { } pending)
                {
                    continue;
                }

                reminders.Add(new Reminder(ReminderKind.RecurrenceAdjustment, $"adjustment:{rule.RecurrenceId}:{pending.Period}",
                    ReminderUrgency.Soon, pending.On, adjustment.Adjust(pending.Base) ?? pending.Base, $"Reajuste de {rule.Description}",
                    RecurrenceId: rule.RecurrenceId, Period: pending.Period, PreviousAmount: pending.Base,
                    Rate: adjustment.EstimatedRate, Index: adjustment.Index));
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

        // MET-9 (ADR-0013, regra 10): meta do mes fora (urgente, como o orcamento estourado). A chave e por escopo
        // e mes: dispensar vale ate o mes seguinte; voltar a "dentro" tira o aviso.
        foreach (var goal in (monthlyGoals ?? []).Where(g => g.Situation == MonthlyGoalSituation.Outside))
        {
            var scope = goal.Scope == GoalScope.Business ? "PJ" : "PF";
            var ceiling = goal.Kind == MonthlyGoalKind.SpendCeiling;
            reminders.Add(new Reminder(ReminderKind.MonthlyGoal, $"goal:{goal.Scope.ToString().ToLowerInvariant()}:{month}:outside",
                ReminderUrgency.Overdue, endOfMonth, ceiling ? goal.Figures.Outflows : goal.Figures.Result,
                $"Meta do mês ({scope})", Limit: goal.Target, Period: month, Scope: goal.Scope.ToString().ToLowerInvariant()));
        }

        // MET-9: objetivo atrasado a 3 meses ou menos do prazo (em breve). A chave muda a cada mes.
        foreach (var objective in (objectives ?? []).Where(o => o.Pace.Situation == SavingsObjectiveSituation.Behind
                     && o.Pace.MonthsLeft is <= ObjectiveWarningMonths && o.Deadline is not null))
        {
            var deadlineEnd = objective.Deadline!.Value.AddMonths(1).AddDays(-1);
            reminders.Add(new Reminder(ReminderKind.SavingsObjective, $"objective:{objective.ObjectiveId}:{month}:behind",
                ReminderUrgency.Soon, deadlineEnd, objective.Pace.NeededPerMonth ?? 0m, $"Objetivo {objective.Name}",
                Limit: objective.Pace.Pace, ObjectiveId: objective.ObjectiveId, Scope: objective.Scope.ToString().ToLowerInvariant()));
        }

        return reminders.OrderBy(r => r.Urgency).ThenBy(r => r.DueOn).ThenBy(r => r.Title, StringComparer.Ordinal).ToList();
    }
}
