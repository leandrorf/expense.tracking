using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Invoices;

/// <summary>RCN-2 (ADR-0011, decisao 7): conferencias guardadas das faturas.</summary>
public interface IInvoiceReconciliationStore
{
    Task<InvoiceReconciliation?> FindAsync(Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceReconciliation>> ListAsync(Guid ownerUserId, Guid cardId, CancellationToken cancellationToken);

    void Add(InvoiceReconciliation reconciliation);

    void Remove(InvoiceReconciliation reconciliation);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>A conferencia guardada: total do banco, total do app naquele momento, a diferenca aceita e a data.</summary>
public sealed record InvoiceReconciliationDto(decimal BankTotal, decimal AppTotal, decimal Difference, DateTimeOffset ReconciledAtUtc)
{
    public static InvoiceReconciliationDto? From(InvoiceReconciliation? r) =>
        r is null ? null : new(r.BankTotal, r.AppTotal, r.Difference, r.ReconciledAtUtc);
}

/// <summary>
/// RCN-2: a fatura vista para conferir. <see cref="AppTotal"/> e o total calculado agora; <see cref="BankTotal"/> e
/// <see cref="Difference"/> (banco - app agora) vem da conferencia guardada, quando ha.
/// </summary>
public sealed record InvoiceReconciliationViewDto(
    string DueMonth, decimal AppTotal, decimal? BankTotal, decimal? Difference, InvoiceReconciliationDto? Reconciliation);

/// <summary>
/// RCN-2 (ADR-0011, decisoes 7 e 8): ver, marcar e desfazer a conferencia de uma fatura pelo total do banco. O
/// total do app e o da fatura calculada (<see cref="CardInvoiceSource"/>); mes sem compra = 0.
/// </summary>
public sealed class InvoiceReconciliationHandler(
    ICardDirectory cards,
    IProjectionQueries projection,
    IInvoiceReconciliationStore reconciliations,
    InvoiceOptions options,
    TimeProvider timeProvider)
{
    public static readonly Error NotFound = Error.NotFound("reconciliation.not_found", "Esta fatura nao foi conferida.");

    public async Task<Result<InvoiceReconciliationViewDto>> GetAsync(
        Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken)
    {
        var total = await AppTotalAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (total.IsFailure)
        {
            return Result.Failure<InvoiceReconciliationViewDto>(total.Error);
        }

        var stored = await reconciliations.FindAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        return View(dueMonth, total.Value, stored);
    }

    public async Task<Result<InvoiceReconciliationViewDto>> ReconcileAsync(
        Guid ownerUserId, Guid cardId, DateOnly dueMonth, decimal bankTotal, CancellationToken cancellationToken)
    {
        var total = await AppTotalAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (total.IsFailure)
        {
            return Result.Failure<InvoiceReconciliationViewDto>(total.Error);
        }

        var now = timeProvider.GetUtcNow();
        var stored = await reconciliations.FindAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (stored is null)
        {
            var created = InvoiceReconciliation.Create(ownerUserId, cardId, dueMonth, bankTotal, total.Value, now);
            if (created.IsFailure)
            {
                return Result.Failure<InvoiceReconciliationViewDto>(created.Error);
            }

            stored = created.Value;
            reconciliations.Add(stored);
        }
        else
        {
            var updated = stored.Update(bankTotal, total.Value, now);
            if (updated.IsFailure)
            {
                return Result.Failure<InvoiceReconciliationViewDto>(updated.Error);
            }
        }

        await reconciliations.SaveChangesAsync(cancellationToken);
        return View(dueMonth, total.Value, stored);
    }

    public async Task<Result> UndoAsync(Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken)
    {
        if (await cards.FindAsync(cardId, cancellationToken) is null)
        {
            return Result.Failure(GetCardInvoicesHandler.CardNotFound);
        }

        var stored = await reconciliations.FindAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (stored is null)
        {
            return Result.Failure(NotFound);
        }

        reconciliations.Remove(stored);
        await reconciliations.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Total da fatura calculada do mes (cartao que o accounts nao devolve ao dono = 404).</summary>
    private async Task<Result<decimal>> AppTotalAsync(Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(cardId, cancellationToken);
        if (card is null)
        {
            return Result.Failure<decimal>(GetCardInvoicesHandler.CardNotFound);
        }

        var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, CardInvoiceSource.Today(timeProvider, options), cancellationToken);
        return loaded.Invoices.FirstOrDefault(i => i.Period.DueMonth == CardCycle.FirstDayOf(dueMonth))?.Total ?? 0m;
    }

    private static InvoiceReconciliationViewDto View(DateOnly dueMonth, decimal appTotal, InvoiceReconciliation? stored) => new(
        dueMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        appTotal,
        stored?.BankTotal,
        stored is null ? null : stored.BankTotal - appTotal,
        InvoiceReconciliationDto.From(stored));
}
