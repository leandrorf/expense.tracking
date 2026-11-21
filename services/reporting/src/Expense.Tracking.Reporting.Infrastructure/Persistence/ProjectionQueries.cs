using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

/// <summary>
/// EVT-3: consulta a projecao com os mesmos filtros que o transactions aplicava (conta e periodo
/// inclusivo pela data do lancamento), sempre restrita ao dono.
/// </summary>
internal sealed class ProjectionQueries(ReportingDbContext dbContext) : IProjectionQueries
{
    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(
        Guid ownerUserId, ReportFilter filter, CancellationToken cancellationToken)
    {
        var query = dbContext.Transactions.AsNoTracking().Where(t => t.OwnerUserId == ownerUserId);

        if (filter.AccountId is { } accountId)
        {
            query = query.Where(t => t.AccountId == accountId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(t => t.OccurredOn >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.OccurredOn <= to);
        }

        return await query
            .Select(t => new TransactionRecord(
                t.AccountId, t.TransactionType, t.PaymentMethod, t.Category, t.Amount, t.Currency, t.AccountScope))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CardTransactionRecord>> GetCardTransactionsAsync(
        Guid ownerUserId, Guid cardId, CancellationToken cancellationToken) =>
        await dbContext.Transactions.AsNoTracking()
            .Where(t => t.OwnerUserId == ownerUserId && t.CreditCardId == cardId)
            .Select(t => new CardTransactionRecord(
                t.TransactionId, t.TransactionType, t.Category, t.Description, t.Amount, t.Currency, t.OccurredOn, t.Installments))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CardPaymentRecord>> GetCardPaymentsAsync(
        Guid ownerUserId, Guid cardId, CancellationToken cancellationToken) =>
        await dbContext.CardPayments.AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId && p.CreditCardId == cardId)
            .Select(p => new CardPaymentRecord(p.PaymentId, p.InvoiceDueMonth, p.AccountId, p.Amount, p.PaidOn))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CategoryUsage>> GetCategoryUsageAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Transactions.AsNoTracking()
            .Where(t => t.OwnerUserId == ownerUserId)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new CategoryUsage(r.Category, r.Count)).ToList();
    }
}
