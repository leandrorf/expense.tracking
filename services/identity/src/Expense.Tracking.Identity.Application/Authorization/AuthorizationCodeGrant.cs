using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Application.OAuth;
using Expense.Tracking.Identity.Domain.Authorization;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Application.Authorization;

public sealed record AuthorizationCodeGrantCommand(
    string ClientId,
    string? ClientSecret,
    string Code,
    string RedirectUri,
    string? CodeVerifier);

/// <summary>Troca um authorization code por tokens (grant_type=authorization_code).</summary>
public sealed class AuthorizationCodeGrantHandler(
    IClientRepository clientRepository,
    IAuthorizationCodeRepository codeRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    ISecureTokenGenerator tokenGenerator,
    IPkceService pkceService,
    IAccessTokenGenerator accessTokenGenerator,
    IUnitOfWork unitOfWork,
    OAuthOptions options,
    TimeProvider timeProvider)
{
    public async Task<Result<TokenResponse>> HandleAsync(AuthorizationCodeGrantCommand command, CancellationToken cancellationToken)
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

        if (!client.AllowsGrant(GrantType.AuthorizationCode))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.UnauthorizedClient("Grant nao permitido para o cliente."));
        }

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidRequest("code e obrigatorio."));
        }

        var now = timeProvider.GetUtcNow();
        var code = await codeRepository.GetByHashAsync(tokenHasher.Hash(command.Code), cancellationToken);

        if (code is null || !code.IsUsable(now))
        {
            // Se o codigo ja foi consumido, e sinal de possivel replay.
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("Authorization code invalido, expirado ou ja utilizado."));
        }

        if (!string.Equals(code.ClientId, client.ClientId, StringComparison.Ordinal))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("O codigo nao pertence a este cliente."));
        }

        if (!string.Equals(code.RedirectUri, command.RedirectUri, StringComparison.Ordinal))
        {
            return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("redirect_uri nao confere com o da autorizacao."));
        }

        // Validacao do PKCE.
        if (!string.IsNullOrEmpty(code.CodeChallenge))
        {
            if (string.IsNullOrWhiteSpace(command.CodeVerifier))
            {
                return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("code_verifier e obrigatorio."));
            }

            if (!pkceService.Verify(command.CodeVerifier, code.CodeChallenge, code.CodeChallengeMethod))
            {
                return Result.Failure<TokenResponse>(OAuthErrors.InvalidGrant("code_verifier invalido."));
            }
        }

        // Consome o codigo (uso unico).
        code.Consume(now);
        codeRepository.Update(code);

        var scopes = ScopeParser.Parse(code.Scopes);
        var accessToken = accessTokenGenerator.Generate(
            new AccessTokenDescriptor(code.UserId.ToString(), client.ClientId, scopes));

        // Emite refresh token (armazenado como hash).
        var rawRefreshToken = tokenGenerator.Generate(48);
        var refreshToken = RefreshToken.Issue(
            tokenHash: tokenHasher.Hash(rawRefreshToken),
            clientId: client.ClientId,
            userId: code.UserId,
            scopes: code.Scopes,
            nowUtc: now,
            lifetime: options.RefreshTokenLifetime);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TokenResponse(
            accessToken.Value,
            accessToken.TokenType,
            accessToken.ExpiresInSeconds,
            code.Scopes,
            rawRefreshToken);
    }
}
