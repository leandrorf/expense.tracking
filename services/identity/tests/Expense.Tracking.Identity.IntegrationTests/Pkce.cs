using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.IntegrationTests;

/// <summary>Gera par verifier/challenge do PKCE (S256) para os testes.</summary>
public static class Pkce
{
    public static (string Verifier, string Challenge) CreateS256()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }
}
