using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Users;

namespace Expense.Tracking.Identity.Application.Abstractions;

/// <summary>Portas de persistencia (repositorios). Implementadas por adaptadores de infraestrutura.</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);
}

public interface IClientRepository
{
    Task<OAuthClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken);
}

public interface IAuthorizationCodeRepository
{
    Task AddAsync(AuthorizationCode code, CancellationToken cancellationToken);

    Task<AuthorizationCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken);

    void Update(AuthorizationCode code);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Update(RefreshToken token);
}

/// <summary>Confirma as alteracoes acumuladas na unidade de trabalho atual.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
