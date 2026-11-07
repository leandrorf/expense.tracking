namespace Expense.Tracking.Identity.Domain.Authorization;

/// <summary>Metodos de code challenge do PKCE (RFC 7636).</summary>
public static class CodeChallengeMethod
{
    public const string S256 = "S256";
    public const string Plain = "plain";

    public static bool IsSupported(string? method) =>
        method is S256 or Plain;
}
