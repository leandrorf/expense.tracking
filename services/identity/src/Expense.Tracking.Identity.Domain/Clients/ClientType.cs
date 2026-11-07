namespace Expense.Tracking.Identity.Domain.Clients;

/// <summary>
/// Tipo do cliente OAuth2:
/// - Public: nao guarda segredo (ex.: SPA React) e DEVE usar PKCE.
/// - Confidential: autentica com client_secret (ex.: servico backend).
/// </summary>
public enum ClientType
{
    Public = 0,
    Confidential = 1
}
