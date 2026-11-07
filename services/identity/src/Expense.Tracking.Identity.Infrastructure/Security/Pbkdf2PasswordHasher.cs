using System.Security.Cryptography;
using Expense.Tracking.Identity.Application.Abstractions;

namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>
/// Hashing de senha com PBKDF2 (HMAC-SHA256), usando apenas a BCL.
/// Formato persistido: "pbkdf2-sha256$&lt;iteracoes&gt;$&lt;salt_b64&gt;$&lt;hash_b64&gt;".
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;      // 128 bits
    private const int KeySize = 32;       // 256 bits
    private const int Iterations = 100_000;
    private const string Prefix = "pbkdf2-sha256";
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encodedHash))
        {
            return false;
        }

        try
        {
            var parts = encodedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix)
            {
                return false;
            }

            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
