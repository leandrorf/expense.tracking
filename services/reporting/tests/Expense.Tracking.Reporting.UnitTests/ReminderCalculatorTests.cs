using Expense.Tracking.Reporting.Domain.Invoices;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>NTF-2 (ADR-0009, decisao 3): o que vira aviso, com que chave, urgencia e ordem.</summary>
public sealed class ReminderCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private static readonly Guid Card = Guid.NewGuid();

    private static ReminderInvoice Invoice(string due, InvoiceStatus status, decimal remaining, decimal total = 800m)
    {
        var dueOn = DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture);
        var period = new InvoicePeriod(new DateOnly(dueOn.Year, dueOn.Month, 1), dueOn.AddDays(-37), dueOn.AddDays(-7), dueOn);
        return new ReminderInvoice(Card, "Nubank", "1234", new Invoice(period, total, total - remaining, remaining, status, []));
    }

    private static ReminderRecurrence Rule(string description, bool isExpense, string startsOn, params string[] closed) =>
        new(Guid.NewGuid(), description, isExpense, 2000m, "Monthly",
            DateOnly.Parse(startsOn, System.Globalization.CultureInfo.InvariantCulture), null, closed.ToHashSet());

    [Fact]
    public void Invoices_DueWithinTheLeadDays_OrOverdueUpTo30Days_AreReminders()
    {
        ReminderInvoice[] invoices =
        [
            Invoice("2026-11-23", InvoiceStatus.Closed, 800m),
            Invoice("2026-11-21", InvoiceStatus.PartiallyPaid, 500m),
            Invoice("2026-11-25", InvoiceStatus.Closed, 800m),   // alem de 3 dias
            Invoice("2026-11-01", InvoiceStatus.Overdue, 100m),
            Invoice("2026-10-01", InvoiceStatus.Overdue, 100m),  // vencida ha mais de 30 dias
            Invoice("2026-11-22", InvoiceStatus.Paid, 0m),
            Invoice("2026-11-22", InvoiceStatus.Open, 300m),     // ainda recebe compras
        ];

        var reminders = ReminderCalculator.Calculate(Today, 3, invoices, []);

        Assert.Equal(
            [(ReminderKind.InvoiceOverdue, ReminderUrgency.Overdue, 100m, $"invoice:{Card}:2026-11:overdue"),
             (ReminderKind.InvoiceDue, ReminderUrgency.Today, 500m, $"invoice:{Card}:2026-11:due"),
             (ReminderKind.InvoiceDue, ReminderUrgency.Soon, 800m, $"invoice:{Card}:2026-11:due")],
            reminders.Select(r => (r.Kind, r.Urgency, r.Amount, r.Key)));
        Assert.All(reminders, r => Assert.Equal((Card, "2026-11", "Fatura Nubank •••• 1234"), (r.CardId!.Value, r.DueMonth, r.Title)));
    }

    [Fact]
    public void Recurrences_WarnAboutExpensesDueSoon_AndGroupWhatIsLeftToLaunch()
    {
        var rent = Rule("Aluguel", isExpense: true, "2026-01-23", "2026-09");
        // Receita nao avisa por vencer; os periodos passados ja foram lancados.
        var salary = Rule("Salario", isExpense: false, "2026-01-22", "2026-09", "2026-10");

        var reminders = ReminderCalculator.Calculate(Today, 3, [], [rent, salary]);

        Assert.Equal(2, reminders.Count);
        var toLaunch = reminders[0];
        Assert.Equal((ReminderKind.RecurrencesToLaunch, ReminderUrgency.Overdue, 1, new DateOnly(2026, 10, 23), "to-launch:2026-10-23"),
            (toLaunch.Kind, toLaunch.Urgency, toLaunch.Count, toLaunch.DueOn, toLaunch.Key));
        var due = reminders[1];
        Assert.Equal((ReminderKind.RecurrenceDue, ReminderUrgency.Soon, "Aluguel", 2000m, rent.RecurrenceId, "2026-11", $"recurrence:{rent.RecurrenceId}:2026-11"),
            (due.Kind, due.Urgency, due.Title, due.Amount, due.RecurrenceId!.Value, due.Period, due.Key));
    }

    [Fact]
    public void Budgets_WarnInAttentionOrExceeded_WithAKeyThatChangesWithTheSituation()
    {
        // NTF-9.
        var (warning, exceeded, fine) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Expense.Tracking.Reporting.Domain.Budgets.BudgetMonthStatus Status(Guid id, string category, decimal spent,
            Expense.Tracking.Reporting.Domain.Budgets.BudgetHealth health) =>
            new(id, category, Expense.Tracking.Reporting.Domain.Budgets.BudgetScope.Business, "BRL", 1000m, spent, 1000m - spent,
                spent / 10m, health, 80);

        var reminders = ReminderCalculator.Calculate(Today, 3, [], [], budgets:
        [
            Status(warning, "Software", 850m, Expense.Tracking.Reporting.Domain.Budgets.BudgetHealth.Warning),
            Status(exceeded, "Viagem", 1200m, Expense.Tracking.Reporting.Domain.Budgets.BudgetHealth.Exceeded),
            Status(fine, "Contador", 100m, Expense.Tracking.Reporting.Domain.Budgets.BudgetHealth.OnTrack),
        ]);

        Assert.Equal(
            [(ReminderKind.Budget, ReminderUrgency.Overdue, $"budget:{exceeded}:2026-11:exceeded", "Orçamento Viagem (PJ)", 1200m, 1000m),
             (ReminderKind.Budget, ReminderUrgency.Soon, $"budget:{warning}:2026-11:warning", "Orçamento Software (PJ)", 850m, 1000m)],
            reminders.Select(r => (r.Kind, r.Urgency, r.Key, r.Title, r.Amount, r.Limit)));
        Assert.All(reminders, r => Assert.Equal(new DateOnly(2026, 11, 30), r.DueOn));
    }

    [Fact]
    public void ResolvedItems_AndDisabledKinds_DoNotWarn()
    {
        var launched = Rule("Aluguel", isExpense: true, "2026-01-23", "2026-09", "2026-10", "2026-11");

        Assert.Empty(ReminderCalculator.Calculate(Today, 3, [], [launched]));
        Assert.Empty(ReminderCalculator.Calculate(Today, 3, [Invoice("2026-11-23", InvoiceStatus.Closed, 800m)], [],
            includeInvoices: false));
        // Antecedencia 0: so o que vence hoje.
        Assert.Empty(ReminderCalculator.Calculate(Today, 0, [Invoice("2026-11-22", InvoiceStatus.Closed, 800m)], []));
    }
}
