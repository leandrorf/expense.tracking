using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Repositories;

internal sealed class ClientRepository(IdentityDbContext dbContext) : IClientRepository
{
    public Task<OAuthClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken) =>
        dbContext.Clients.FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
}
