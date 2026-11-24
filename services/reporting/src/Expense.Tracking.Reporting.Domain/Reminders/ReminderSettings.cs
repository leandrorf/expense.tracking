using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Reminders;

/// <summary>
/// NTF-6 (ADR-0009, decisao 4): preferencias dos avisos de um usuario. Sem linha gravada, valem os padroes
/// (<see cref="Default"/>): 3 dias de antecedencia e todos os tipos ligados.
/// </summary>
public sealed class ReminderSettings
{
    private ReminderSettings()
    {
    }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Dias corridos de antecedencia (0 = so o que vence hoje).</summary>
    public int LeadDays { get; private set; } = ReminderCalculator.DefaultLeadDays;

    /// <summary>Avisos de fatura (a vencer e vencida).</summary>
    public bool Invoices { get; private set; } = true;

    /// <summary>Avisos de recorrencia (a vencer e "a lancar").</summary>
    public bool Recurrences { get; private set; } = true;

    /// <summary>NTF-9: avisos de orcamento em Atencao ou Estourado.</summary>
    public bool Budgets { get; private set; } = true;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ReminderSettings Default(Guid ownerUserId) => new() { OwnerUserId = ownerUserId };

    public Result Update(int leadDays, bool invoices, bool recurrences, bool budgets, DateTimeOffset nowUtc)
    {
        if (leadDays is < 0 or > ReminderCalculator.MaxLeadDays)
        {
            return Result.Failure(Error.Validation(
                "reminders.lead_days_invalid", $"A antecedencia vai de 0 a {ReminderCalculator.MaxLeadDays} dias."));
        }

        LeadDays = leadDays;
        Invoices = invoices;
        Recurrences = recurrences;
        Budgets = budgets;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
