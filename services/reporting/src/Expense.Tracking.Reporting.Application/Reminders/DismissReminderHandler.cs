using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <summary>
/// NTF-5 (ADR-0009, decisao 5): dispensa um aviso pela chave dele. O aviso some ate a chave mudar (a
/// fatura que vence sem pagamento volta como "vencida"). Aproveita para apagar as dispensas velhas.
/// </summary>
public sealed class DismissReminderHandler(IReminderStore store, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid ownerUserId, string? key, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dismissal = ReminderDismissal.Create(ownerUserId, key, now);
        if (dismissal.IsFailure)
        {
            return Result.Failure(dismissal.Error);
        }

        await store.DismissAsync(dismissal.Value, now - ReminderDismissal.RetentionPeriod, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
