using Expense.Tracking.Reporting.Application.Reminders;
using Expense.Tracking.Reporting.Domain.Reminders;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Reporting.Infrastructure.Persistence;

internal sealed class ReminderStore(ReportingDbContext dbContext) : IReminderStore
{
    public async Task<IReadOnlySet<string>> ListDismissedKeysAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await dbContext.ReminderDismissals.AsNoTracking()
            .Where(d => d.OwnerUserId == ownerUserId)
            .Select(d => d.Key)
            .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public async Task DismissAsync(ReminderDismissal dismissal, DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var mine = await dbContext.ReminderDismissals
            .Where(d => d.OwnerUserId == dismissal.OwnerUserId && (d.Key == dismissal.Key || d.DismissedAtUtc < olderThan))
            .ToListAsync(cancellationToken);
        dbContext.ReminderDismissals.RemoveRange(mine.Where(d => d.Key != dismissal.Key));
        if (mine.All(d => d.Key != dismissal.Key))
        {
            dbContext.ReminderDismissals.Add(dismissal);
        }
    }

    public Task<ReminderSettings?> FindSettingsAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        dbContext.ReminderSettings.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, cancellationToken);

    public void AddSettings(ReminderSettings settings) => dbContext.ReminderSettings.Add(settings);

    public async Task<IReadOnlyList<ReminderSettings>> ListDigestCandidatesAsync(CancellationToken cancellationToken) =>
        await dbContext.ReminderSettings
            .Where(s => s.EmailEnabled && s.Email != null && s.EmailConfirmedAtUtc != null)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
