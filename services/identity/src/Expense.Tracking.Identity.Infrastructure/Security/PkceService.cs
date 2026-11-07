using System.Security.Cryptography;
using System.Text;
using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>Verificacao do PKCE (RFC 7636): S256 e plain.</summary>
public sealed class PkceService : IPkceService
{
    public bool Verify(string codeVerifier, string codeChallenge, string method)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
        {
            return false;
        }

        var computed = method switch
        {
            CodeChallengeMethod.S256 => ComputeS256(codeVerifier),
            CodeChallengeMethod.Plain => codeVerifier,
            _ => null
        };

        if (computed is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(codeChallenge));
    }

    private static string ComputeS256(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncoder.Encode(hash);
    }
}
