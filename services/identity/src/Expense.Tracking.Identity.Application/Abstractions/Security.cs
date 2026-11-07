namespace Expense.Tracking.Identity.Application.Abstractions;

/// <summary>Hashing de senha e de segredos de cliente (adaptador: PBKDF2).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string encodedHash);
}

/// <summary>Gera tokens opacos aleatorios (codigos de autorizacao, refresh tokens).</summary>
public interface ISecureTokenGenerator
{
    /// <summary>Retorna um token url-safe com a entropia indicada em bytes.</summary>
    string Generate(int numberOfBytes = 32);
}

/// <summary>Produz um hash estavel (SHA-256) para armazenar codigos/refresh tokens sem o valor em claro.</summary>
public interface ITokenHasher
{
    string Hash(string token);
}

/// <summary>Verificacao do PKCE (RFC 7636).</summary>
public interface IPkceService
{
    bool Verify(string codeVerifier, string codeChallenge, string method);
}

/// <summary>Descritor para emissao de um access token JWT.</summary>
public sealed record AccessTokenDescriptor(
    string Subject,
    string ClientId,
    IReadOnlyCollection<string> Scopes,
    bool IsClientCredentials = false,
    IReadOnlyDictionary<string, string>? AdditionalClaims = null);

public sealed record AccessTokenResult(
    string Value,
    DateTimeOffset ExpiresAtUtc,
    int ExpiresInSeconds,
    string TokenType = "Bearer");

/// <summary>Gera access tokens JWT assinados (adaptador: RS256).</summary>
public interface IAccessTokenGenerator
{
    AccessTokenResult Generate(AccessTokenDescriptor descriptor);
}
