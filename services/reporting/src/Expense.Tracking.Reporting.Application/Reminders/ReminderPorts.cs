using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <summary>NTF (ADR-0009, decisoes 4 e 5): o que o usuario grava sobre os avisos.</summary>
public interface IReminderStore
{
    /// <summary>Chaves dos avisos que o usuario dispensou.</summary>
    Task<IReadOnlySet<string>> ListDismissedKeysAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Grava a dispensa (a mesma chave duas vezes nao duplica) e apaga as do usuario mais velhas que <paramref name="olderThan"/>.</summary>
    Task DismissAsync(ReminderDismissal dismissal, DateTimeOffset olderThan, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
