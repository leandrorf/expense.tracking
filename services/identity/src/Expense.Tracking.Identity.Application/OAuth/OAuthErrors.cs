using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Application.OAuth;

/// <summary>
/// Catalogo de erros padronizados do OAuth2 (RFC 6749 secao 5.2). O codigo do <see cref="Error"/>
/// corresponde ao campo "error" da resposta; o <see cref="ErrorType"/> define o status HTTP.
/// </summary>
public static class OAuthErrors
{
    public static Error InvalidRequest(string description) =>
        new("invalid_request", description, ErrorType.Validation);

    public static Error InvalidClient(string description) =>
        new("invalid_client", description, ErrorType.Unauthorized);

    public static Error InvalidGrant(string description) =>
        new("invalid_grant", description, ErrorType.Validation);

    public static Error UnauthorizedClient(string description) =>
        new("unauthorized_client", description, ErrorType.Forbidden);

    public static Error UnsupportedGrantType(string description) =>
        new("unsupported_grant_type", description, ErrorType.Validation);

    public static Error InvalidScope(string description) =>
        new("invalid_scope", description, ErrorType.Validation);

    public static Error AccessDenied(string description) =>
        new("access_denied", description, ErrorType.Forbidden);
}
