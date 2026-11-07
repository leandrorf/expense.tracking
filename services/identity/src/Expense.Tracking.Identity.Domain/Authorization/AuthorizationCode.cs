namespace Expense.Tracking.Identity.Domain.Authorization;

/// <summary>
/// Codigo de autorizacao (grant authorization_code). Guardamos apenas o HASH do codigo
/// emitido ao cliente; o valor em claro nunca e persistido. Uso unico e com expiracao curta.
/// </summary>
public sealed class AuthorizationCode
{
    private AuthorizationCode(
        Guid id,
        string codeHash,
        string clientId,
        Guid userId,
        string redirectUri,
        string scopes,
        string codeChallenge,
        string codeChallengeMethod,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? consumedAtUtc)
    {
        Id = id;
        CodeHash = codeHash;
        ClientId = clientId;
        UserId = userId;
        RedirectUri = redirectUri;
        Scopes = scopes;
        CodeChallenge = codeChallenge;
        CodeChallengeMethod = codeChallengeMethod;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ConsumedAtUtc = consumedAtUtc;
    }

    public Guid Id { get; private set; }

    public string CodeHash { get; private set; }

    public string ClientId { get; private set; }

    public Guid UserId { get; private set; }

    public string RedirectUri { get; private set; }

    /// <summary>Escopos concedidos, separados por espaco.</summary>
    public string Scopes { get; private set; }

    public string CodeChallenge { get; private set; }

    public string CodeChallengeMethod { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc is not null;

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public bool IsUsable(DateTimeOffset nowUtc) => !IsConsumed && !IsExpired(nowUtc);

    public static AuthorizationCode Issue(
        string codeHash,
        string clientId,
        Guid userId,
        string redirectUri,
        string scopes,
        string codeChallenge,
        string codeChallengeMethod,
        DateTimeOffset nowUtc,
        TimeSpan lifetime)
    {
        return new AuthorizationCode(
            id: Guid.NewGuid(),
            codeHash: codeHash,
            clientId: clientId,
            userId: userId,
            redirectUri: redirectUri,
            scopes: scopes,
            codeChallenge: codeChallenge,
            codeChallengeMethod: codeChallengeMethod,
            createdAtUtc: nowUtc,
            expiresAtUtc: nowUtc.Add(lifetime),
            consumedAtUtc: null);
    }

    public void Consume(DateTimeOffset nowUtc) => ConsumedAtUtc = nowUtc;
}
