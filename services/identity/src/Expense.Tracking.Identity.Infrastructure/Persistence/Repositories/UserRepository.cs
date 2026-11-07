using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(IdentityDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users.FirstOrDefaultAsync(u => u.Email == Email.FromTrusted(normalizedEmail), cancellationToken);

    public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(u => u.Email == Email.FromTrusted(normalizedEmail), cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await dbContext.Users.AddAsync(user, cancellationToken);
}
