using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Api.Setup;

/// <summary>Mapeia <see cref="Error"/> de dominio para respostas HTTP.</summary>
public static class HttpResults
{
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>Resposta de erro em formato ProblemDetails (RFC 7807) para endpoints de conta.</summary>
    public static IResult Problem(Error error) =>
        Microsoft.AspNetCore.Http.Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: StatusCodeFor(error.Type));

    /// <summary>Resposta de erro no formato do OAuth2 (RFC 6749 secao 5.2).</summary>
    public static IResult OAuthError(Error error)
    {
        var status = StatusCodeFor(error.Type);
        var payload = new Dictionary<string, string>
        {
            ["error"] = string.IsNullOrEmpty(error.Code) ? "invalid_request" : error.Code,
            ["error_description"] = error.Description
        };

        return Microsoft.AspNetCore.Http.Results.Json(payload, statusCode: status);
    }
}
