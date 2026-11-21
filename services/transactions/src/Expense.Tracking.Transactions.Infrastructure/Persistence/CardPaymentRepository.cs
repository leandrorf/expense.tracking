using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.CardPayments;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class CardPaymentRepository(TransactionsDbContext dbContext) : ICardPaymentRepository
{
    public Task<CardPayment?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.CardPayments.FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<IReadOnlyList<CardPayment>> ListAsync(
        Guid ownerUserId, Guid? creditCardId, DateOnly? invoiceDueMonth, CancellationToken cancellationToken)
    {
        var query = dbContext.CardPayments.Where(p => p.OwnerUserId == ownerUserId);
        if (creditCardId is { } cardId)
        {
            query = query.Where(p => p.CreditCardId == cardId);
        }

        if (invoiceDueMonth is { } month)
        {
            var firstDay = new DateOnly(month.Year, month.Month, 1);
            query = query.Where(p => p.InvoiceDueMonth == firstDay);
        }

        return await query
            .OrderByDescending(p => p.PaidOn)
            .ThenByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CardPayment payment, CancellationToken cancellationToken) =>
        await dbContext.CardPayments.AddAsync(payment, cancellationToken);

    public void Remove(CardPayment payment) => dbContext.CardPayments.Remove(payment);
}
