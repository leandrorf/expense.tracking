namespace Expense.Tracking.Identity.Application.Abstractions;

/// <summary>
/// Configuracao do Identity Server. Injetada como singleton (bind a partir da configuracao na API).
/// </summary>
public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>Issuer (iss) dos tokens. Ex.: https://localhost:5001</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Audience padrao dos access tokens das APIs de recurso.</summary>
    public string DefaultAudience { get; set; } = "expense-tracking-api";

    public int AccessTokenLifetimeSeconds { get; set; } = 3600;

    public int AuthorizationCodeLifetimeSeconds { get; set; } = 120;

    public int RefreshTokenLifetimeDays { get; set; } = 14;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromSeconds(AccessTokenLifetimeSeconds);

    public TimeSpan AuthorizationCodeLifetime => TimeSpan.FromSeconds(AuthorizationCodeLifetimeSeconds);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenLifetimeDays);
}
