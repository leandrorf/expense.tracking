using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Application.OAuth;
using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Application.Authorization;

public sealed record RefreshTokenGrantCommand(
    string ClientId,
    string? ClientSecret,
    string RefreshToken,
    string? Scope);

/// <summary>
/// Renova tokens via refresh token (grant_type=refresh_token) com ROTACAO:
/// o token usado e revogado e substituido por um novo.
/// </summary>
public sealed class RefreshTokenGrantHandler(
    IClientRepository clientRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator tokenGenerator,
    IAccessTokenGenerator accessTokenGenerator,
    IUnitOfWork unitOfWork,
    OAuthOptions options,
    TimeProvider timeProvider)
{
    public async Task<Result<TokenResponse>> HandleAsync(RefreshTokenGrantCommand command, CancellationToken cancellationToken)
    {
        var client = await clientRepository.GetByClientIdAsync(command.ClientId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidClient("Cliente nao encontrado."));
        }

        var auth = ClientAuthentication.Authenticate(client, command.ClientSecret, passwordHasher);
        if (auth.IsFailure)
        {
            return Result.Failure<TokenResponse>(auth.Error);
        }

        if (!client.AllowsGrant(GrantType.RefreshToken))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.UnauthorizedClient("Grant nao permitido para o cliente."));
        }

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidRequest("refresh_token e obrigatorio."));
        }

        var now = timeProvider.GetUtcNow();
        var existing = await refreshTokenRepository.GetByHashAsync(tokenHasher.Hash(command.RefreshToken), cancellationToken);

        if (existing is null)
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("Refresh token invalido."));
        }

        if (!string.Equals(existing.ClientId, client.ClientId, StringComparison.Ordinal))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("O refresh token nao pertence a este cliente."));
        }

        if (!existing.IsActive(now))
        {
            // Token revogado/expirado. Se revogado e ja substituido, pode indicar reuso (token roubado).
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("Refresh token expirado ou revogado."));
        }

        // Escopo: se solicitado, deve ser subconjunto do originalmente concedido.
        var grantedScopes = ScopeParser.Parse(existing.Scopes);
        var requestedScopes = ScopeParser.Parse(command.Scope);
        if (requestedScopes.Length > 0 && !ScopeParser.IsSubset(requestedScopes, grantedScopes))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidScope("Escopo solicitado excede o concedido originalmente."));
        }

        var effectiveScopes = requestedScopes.Length > 0 ? requestedScopes : grantedScopes;
        var effectiveScope = ScopeParser.Join(effectiveScopes);

        // Rotaciona: cria novo refresh token e revoga/substitui o atual.
        var rawRefreshToken = tokenGenerator.Generate(48);
        var newToken = RefreshToken.Issue(
            tokenHash: tokenHasher.Hash(rawRefreshToken),
            clientId: client.ClientId,
            userId: existing.UserId,
            scopes: effectiveScope,
            nowUtc: now,
            lifetime: options.RefreshTokenLifetime);

        existing.ReplaceWith(newToken.Id, now);
        refreshTokenRepository.Update(existing);
        await refreshTokenRepository.AddAsync(newToken, cancellationToken);

        var accessToken = accessTokenGenerator.Generate(
            new AccessTokenDescriptor(existing.UserId.ToString(), client.ClientId, effectiveScopes));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TokenResponse(
            accessToken.Value,
            accessToken.TokenType,
            accessToken.ExpiresInSeconds,
            effectiveScope,
            rawRefreshToken);
    }
}
