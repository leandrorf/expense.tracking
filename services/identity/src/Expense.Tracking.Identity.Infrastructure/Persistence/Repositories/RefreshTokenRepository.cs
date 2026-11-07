using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(IdentityDbContext dbContext) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public void Update(RefreshToken token) => dbContext.RefreshTokens.Update(token);
}
