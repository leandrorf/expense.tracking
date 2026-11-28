using Expense.Tracking.Reporting.Application.Goals;
using Expense.Tracking.Reporting.Domain.Goals;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

internal sealed class SavingsObjectiveRepository(ReportingDbContext dbContext) : ISavingsObjectiveRepository
{
    public async Task<IReadOnlyList<SavingsObjective>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.SavingsObjectives.AsNoTracking()
            .Where(o => o.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

    public Task<SavingsObjective?> FindAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.SavingsObjectives.FirstOrDefaultAsync(o => o.Id == id && o.OwnerUserId == ownerUserId, cancellationToken);

    public async Task AddAsync(SavingsObjective objective, CancellationToken cancellationToken)
    {
        dbContext.SavingsObjectives.Add(objective);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
