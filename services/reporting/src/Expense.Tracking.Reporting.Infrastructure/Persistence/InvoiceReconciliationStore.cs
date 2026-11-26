using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

internal sealed class InvoiceReconciliationStore(ReportingDbContext dbContext) : IInvoiceReconciliationStore
{
    public Task<InvoiceReconciliation?> FindAsync(Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken)
    {
        var month = new DateOnly(dueMonth.Year, dueMonth.Month, 1);
        return dbContext.InvoiceReconciliations.FirstOrDefaultAsync(
            r => r.OwnerUserId == ownerUserId && r.CardId == cardId && r.DueMonth == month, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceReconciliation>> ListAsync(Guid ownerUserId, Guid cardId, CancellationToken cancellationToken) =>
        await dbContext.InvoiceReconciliations.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.CardId == cardId)
            .ToListAsync(cancellationToken);

    public void Add(InvoiceReconciliation reconciliation) => dbContext.InvoiceReconciliations.Add(reconciliation);

    public void Remove(InvoiceReconciliation reconciliation) => dbContext.InvoiceReconciliations.Remove(reconciliation);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
