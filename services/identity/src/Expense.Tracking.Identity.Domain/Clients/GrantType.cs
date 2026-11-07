namespace Expense.Tracking.Identity.Domain.Clients;

/// <summary>Grant types do OAuth2 suportados pelo Identity Server.</summary>
public static class GrantType
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";
    public const string ClientCredentials = "client_credentials";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AuthorizationCode,
        RefreshToken,
        ClientCredentials
    };

    public static bool IsSupported(string grantType) => All.Contains(grantType);
}
