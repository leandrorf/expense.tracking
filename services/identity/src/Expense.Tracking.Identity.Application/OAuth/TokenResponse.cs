namespace Expense.Tracking.Identity.Application.OAuth;

/// <summary>Resposta de sucesso do endpoint de token (RFC 6749 secao 5.1).</summary>
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string Scope,
    string? RefreshToken = null);
