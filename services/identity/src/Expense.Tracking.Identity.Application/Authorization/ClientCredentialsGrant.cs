using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Application.OAuth;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Application.Authorization;

public sealed record ClientCredentialsGrantCommand(
    string ClientId,
    string? ClientSecret,
    string? Scope);

/// <summary>
/// Emite access token para comunicacao maquina-a-maquina (grant_type=client_credentials).
/// Apenas clientes confidenciais; sem refresh token e sem contexto de usuario.
/// </summary>
public sealed class ClientCredentialsGrantHandler(
    IClientRepository clientRepository,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator)
{
    public async Task<Result<TokenResponse>> HandleAsync(ClientCredentialsGrantCommand command, CancellationToken cancellationToken)
    {
        var client = await clientRepository.GetByClientIdAsync(command.ClientId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidClient("Cliente nao encontrado."));
        }

        if (!client.IsConfidential)
        {
            return Result.Failure<TokenResponse>(OAuthErrors.UnauthorizedClient("client_credentials exige cliente confidencial."));
        }

        var auth = ClientAuthentication.Authenticate(client, command.ClientSecret, passwordHasher);
        if (auth.IsFailure)
        {
            return Result.Failure<TokenResponse>(auth.Error);
        }

        if (!client.AllowsGrant(GrantType.ClientCredentials))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.UnauthorizedClient("Grant nao permitido para o cliente."));
        }

        var requestedScopes = ScopeParser.Parse(command.Scope);
        var scopeCheck = client.EnsureScopesAllowed(requestedScopes);
        if (scopeCheck.IsFailure)
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidScope(scopeCheck.Error.Description));
        }

        var grantedScopes = requestedScopes.Length > 0 ? requestedScopes : client.AllowedScopes.ToArray();
        var grantedScope = ScopeParser.Join(grantedScopes);

        var accessToken = accessTokenGenerator.Generate(
            new AccessTokenDescriptor(client.ClientId, client.ClientId, grantedScopes, IsClientCredentials: true));

        return new TokenResponse(
            accessToken.Value,
            accessToken.TokenType,
            accessToken.ExpiresInSeconds,
            grantedScope);
    }
}
