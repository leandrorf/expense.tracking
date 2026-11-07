namespace Expense.Tracking.Identity.Domain.Authorization;

/// <summary>
/// Refresh token persistido como HASH. Suporta rotacao: ao ser usado, e revogado e
/// substituido por um novo (ReplacedByTokenId), permitindo detectar reuso/roubo.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken(
        Guid id,
        string tokenHash,
        string clientId,
        Guid userId,
        string scopes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        Guid? replacedByTokenId)
    {
        Id = id;
        TokenHash = tokenHash;
        ClientId = clientId;
        UserId = userId;
        Scopes = scopes;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RevokedAtUtc = revokedAtUtc;
        ReplacedByTokenId = replacedByTokenId;
    }

    public Guid Id { get; private set; }

    public string TokenHash { get; private set; }

    public string ClientId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Escopos associados, separados por espaco.</summary>
    public string Scopes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public bool IsActive(DateTimeOffset nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    public static RefreshToken Issue(
        string tokenHash,
        string clientId,
        Guid userId,
        string scopes,
        DateTimeOffset nowUtc,
        TimeSpan lifetime)
    {
        return new RefreshToken(
            id: Guid.NewGuid(),
            tokenHash: tokenHash,
            clientId: clientId,
            userId: userId,
            scopes: scopes,
            createdAtUtc: nowUtc,
            expiresAtUtc: nowUtc.Add(lifetime),
            revokedAtUtc: null,
            replacedByTokenId: null);
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (!IsRevoked)
        {
            RevokedAtUtc = nowUtc;
        }
    }

    public void ReplaceWith(Guid newTokenId, DateTimeOffset nowUtc)
    {
        Revoke(nowUtc);
        ReplacedByTokenId = newTokenId;
    }
}
