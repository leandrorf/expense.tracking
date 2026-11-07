using System.Security.Cryptography;
using System.Text;
using Expense.Tracking.Identity.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>
/// Gera um hash SHA-256 (base64url) para armazenar codigos/refresh tokens sem o valor em claro.
/// Como o valor de entrada ja e aleatorio e de alta entropia, SHA-256 simples e suficiente.
/// </summary>
public sealed class Sha256TokenHasher : ITokenHasher
{
    public string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Base64UrlEncoder.Encode(bytes);
    }
}
