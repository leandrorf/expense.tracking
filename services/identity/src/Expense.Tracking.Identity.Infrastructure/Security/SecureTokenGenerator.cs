using System.Security.Cryptography;
using Expense.Tracking.Identity.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>Gera tokens opacos aleatorios url-safe usando um RNG criptografico.</summary>
public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    public string Generate(int numberOfBytes = 32)
    {
        if (numberOfBytes < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(numberOfBytes), "A entropia minima e de 16 bytes.");
        }

        var bytes = RandomNumberGenerator.GetBytes(numberOfBytes);
        return Base64UrlEncoder.Encode(bytes);
    }
}
