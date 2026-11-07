using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Application.OAuth;
using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Application.Authorization;

public sealed record IssueAuthorizationCodeCommand(
    Guid UserId,
    string ClientId,
    string ResponseType,
    string RedirectUri,
    string? Scope,
    string? CodeChallenge,
    string? CodeChallengeMethod);

public sealed record AuthorizationCodeIssued(string Code, string GrantedScope);

/// <summary>
/// Emite um authorization code para um usuario ja autenticado (fluxo Authorization Code + PKCE).
/// </summary>
public sealed class IssueAuthorizationCodeHandler(
    IClientRepository clientRepository,
    IAuthorizationCodeRepository codeRepository,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IUnitOfWork unitOfWork,
    OAuthOptions options,
    TimeProvider timeProvider)
{
    public async Task<Result<AuthorizationCodeIssued>> HandleAsync(IssueAuthorizationCodeCommand command, CancellationToken cancellationToken)
    {
        if (!string.Equals(command.ResponseType, "code", StringComparison.Ordinal))
        {
            return Result.Failure<AuthorizationCodeIssued>(
                OAuthErrors.InvalidRequest("response_type deve ser 'code'."));
        }

        var client = await clientRepository.GetByClientIdAsync(command.ClientId, cancellationToken);
        if (client is null || !client.IsActive)
        {
            return Result.Failure<AuthorizationCodeIssued>(
                OAuthErrors.InvalidClient("Cliente nao encontrado ou inativo."));
        }

        // redirect_uri deve bater exatamente com um registrado (sem isso, nao redirecionar).
        if (!client.HasRedirectUri(command.RedirectUri))
        {
            return Result.Failure<AuthorizationCodeIssued>(
                OAuthErrors.InvalidRequest("redirect_uri nao registrado para o cliente."));
        }

        if (!client.AllowsGrant(GrantType.AuthorizationCode))
        {
            return Result.Failure<AuthorizationCodeIssued>(
                OAuthErrors.UnauthorizedClient("Cliente nao autorizado ao grant authorization_code."));
        }

        // PKCE: obrigatorio quando o cliente exige (sempre para publicos).
        var method = command.CodeChallengeMethod;
        if (client.RequirePkce)
        {
            if (string.IsNullOrWhiteSpace(command.CodeChallenge))
            {
                return Result.Failure<AuthorizationCodeIssued>(
                    OAuthErrors.InvalidRequest("code_challenge e obrigatorio (PKCE)."));
            }

            method ??= CodeChallengeMethod.S256;
            if (!CodeChallengeMethod.IsSupported(method))
            {
                return Result.Failure<AuthorizationCodeIssued>(
                    OAuthErrors.InvalidRequest("code_challenge_method invalido."));
            }
        }

        var requestedScopes = ScopeParser.Parse(command.Scope);
        var scopeCheck = client.EnsureScopesAllowed(requestedScopes);
        if (scopeCheck.IsFailure)
        {
            return Result.Failure<AuthorizationCodeIssued>(
                OAuthErrors.InvalidScope(scopeCheck.Error.Description));
        }

        var grantedScopes = requestedScopes.Length > 0
            ? requestedScopes
            : client.AllowedScopes.ToArray();
        var grantedScope = ScopeParser.Join(grantedScopes);

        var rawCode = tokenGenerator.Generate();
        var code = AuthorizationCode.Issue(
            codeHash: tokenHasher.Hash(rawCode),
            clientId: client.ClientId,
            userId: command.UserId,
            redirectUri: command.RedirectUri,
            scopes: grantedScope,
            codeChallenge: command.CodeChallenge ?? string.Empty,
            codeChallengeMethod: method ?? string.Empty,
            nowUtc: timeProvider.GetUtcNow(),
            lifetime: options.AuthorizationCodeLifetime);

        await codeRepository.AddAsync(code, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthorizationCodeIssued(rawCode, grantedScope);
    }
}
