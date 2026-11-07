using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Repositories;

internal sealed class AuthorizationCodeRepository(IdentityDbContext dbContext) : IAuthorizationCodeRepository
{
    public async Task AddAsync(AuthorizationCode code, CancellationToken cancellationToken) =>
        await dbContext.AuthorizationCodes.AddAsync(code, cancellationToken);

    public Task<AuthorizationCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken) =>
        dbContext.AuthorizationCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);

    public void Update(AuthorizationCode code) => dbContext.AuthorizationCodes.Update(code);
}
