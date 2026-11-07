using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Api.Setup;

/// <summary>Obtem o access token do request atual para encaminhar ao servico accounts.</summary>
public sealed class HttpContextUserTokenAccessor(IHttpContextAccessor httpContextAccessor) : IUserTokenAccessor
{
    private const string BearerPrefix = "Bearer ";

    public string? GetAccessToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? header[BearerPrefix.Length..].Trim()
            : header;
    }
}
