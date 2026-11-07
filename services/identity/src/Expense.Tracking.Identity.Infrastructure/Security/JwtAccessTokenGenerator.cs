using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Expense.Tracking.Identity.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>Emite access tokens JWT assinados em RS256.</summary>
public sealed class JwtAccessTokenGenerator(
    SigningKeyProvider signingKeyProvider,
    OAuthOptions options,
    TimeProvider timeProvider) : IAccessTokenGenerator
{
    private readonly JwtSecurityTokenHandler _handler = new();

    public AccessTokenResult Generate(AccessTokenDescriptor descriptor)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, descriptor.Subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("client_id", descriptor.ClientId),
            new("token_type", descriptor.IsClientCredentials ? "client" : "user")
        };

        if (descriptor.Scopes.Count > 0)
        {
            claims.Add(new Claim("scope", string.Join(' ', descriptor.Scopes)));
        }

        if (descriptor.AdditionalClaims is not null)
        {
            claims.AddRange(descriptor.AdditionalClaims.Select(kvp => new Claim(kvp.Key, kvp.Value)));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.DefaultAudience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = signingKeyProvider.SigningCredentials
        };

        var token = _handler.CreateEncodedJwt(tokenDescriptor);

        return new AccessTokenResult(
            Value: token,
            ExpiresAtUtc: expiresAt,
            ExpiresInSeconds: options.AccessTokenLifetimeSeconds);
    }
}
