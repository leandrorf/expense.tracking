using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Recurrences;

/// <summary>REC-3 (ADR-0008, decisao 7): o que a exclusao de um lancamento faz com a ocorrencia que ele fechou.</summary>
public static class RecurrenceLinks
{
    /// <summary>
    /// Se o lancamento fechou um periodo de uma regra que ainda existe, reabre o periodo
    /// (<see cref="RecurrenceOccurrenceReopened"/>). Fica pendente no contexto: grava no mesmo commit da exclusao.
    /// </summary>
    public static async Task ReopenLaunchedAsync(
        IRecurrenceRepository recurrences, IEventOutbox outbox, Transaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.RecurrenceId is null)
        {
            return;
        }

        var occurrence = await recurrences.FindOccurrenceByTransactionAsync(transaction.Id, cancellationToken);
        if (occurrence is null)
        {
            return;
        }

        recurrences.RemoveOccurrence(occurrence);
        outbox.Add(new RecurrenceOccurrenceReopened(occurrence.RecurrenceId, occurrence.OwnerUserId, occurrence.Period));
    }
}
