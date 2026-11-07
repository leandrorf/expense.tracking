using Expense.Tracking.Reporting.Application.Abstractions;

namespace Expense.Tracking.Reporting.Api.Setup;

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
