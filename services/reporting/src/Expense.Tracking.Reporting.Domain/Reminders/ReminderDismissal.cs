using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Reminders;

/// <summary>
/// NTF-5 (ADR-0009, decisao 5): um aviso que o usuario dispensou. Guarda so a chave: como ela muda com o
/// estado (a fatura "a vencer" e a "vencida" tem chaves diferentes), o aviso volta quando o estado muda.
/// </summary>
public sealed class ReminderDismissal
{
    public const int KeyMaxLength = 120;

    /// <summary>Dispensa mais velha que isso ja nao esconde nada que ainda avise; e apagada.</summary>
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(120);

    private static readonly string[] KnownPrefixes = ["invoice:", "recurrence:", "to-launch:", "budget:"];

    private ReminderDismissal()
    {
        Key = string.Empty;
    }

    public Guid OwnerUserId { get; private set; }

    public string Key { get; private set; }

    public DateTimeOffset DismissedAtUtc { get; private set; }

    public static Result<ReminderDismissal> Create(Guid ownerUserId, string? key, DateTimeOffset nowUtc)
    {
        var trimmed = key?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > KeyMaxLength || !KnownPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.Ordinal)))
        {
            return Result.Failure<ReminderDismissal>(Error.Validation(
                "reminders.key_invalid", "Informe a chave de um aviso para dispensar."));
        }

        return new ReminderDismissal { OwnerUserId = ownerUserId, Key = trimmed, DismissedAtUtc = nowUtc };
    }
}
