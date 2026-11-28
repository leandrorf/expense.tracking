using Expense.Tracking.Reporting.Application.Goals;
using Expense.Tracking.Reporting.Domain.Goals;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

internal sealed class MonthlyGoalRepository(ReportingDbContext dbContext) : IMonthlyGoalRepository
{
    public async Task<IReadOnlyList<MonthlyGoal>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await dbContext.MonthlyGoals.AsNoTracking()
            .Where(g => g.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

    public Task<MonthlyGoal?> FindOpenAsync(Guid ownerUserId, GoalScope scope, CancellationToken cancellationToken) =>
        dbContext.MonthlyGoals.FirstOrDefaultAsync(
            g => g.OwnerUserId == ownerUserId && g.Scope == scope && g.ClosedFromMonth == null, cancellationToken);

    public Task<DateOnly?> LatestClosedFromAsync(Guid ownerUserId, GoalScope scope, CancellationToken cancellationToken) =>
        dbContext.MonthlyGoals
            .Where(g => g.OwnerUserId == ownerUserId && g.Scope == scope && g.ClosedFromMonth != null)
            .MaxAsync(g => g.ClosedFromMonth, cancellationToken);

    public async Task<bool> TryAddAsync(MonthlyGoal goal, CancellationToken cancellationToken)
    {
        dbContext.MonthlyGoals.Add(goal);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Indice unico (dono, escopo aberto): outra requisicao gravou antes.
            dbContext.Entry(goal).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
