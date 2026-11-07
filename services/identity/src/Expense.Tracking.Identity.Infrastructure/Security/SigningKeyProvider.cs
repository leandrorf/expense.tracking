using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>
/// Mantem a chave RSA usada para assinar (RS256) e verificar os access tokens, e expoe a
/// chave publica no formato JWK para o endpoint JWKS. Registrado como singleton.
/// </summary>
public sealed class SigningKeyProvider : IDisposable
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _privateKey;
    private readonly RsaSecurityKey _publicKey;

    public SigningKeyProvider(OAuthSigningOptions options, ILogger<SigningKeyProvider> logger)
    {
        _rsa = RSA.Create(2048);

        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPem))
        {
            _rsa.ImportFromPem(options.PrivateKeyPem);
        }
        else
        {
            logger.LogWarning(
                "Nenhuma chave de assinatura configurada (OAuth:SigningKey:PrivateKeyPem). " +
                "Gerando chave RSA efemera - NAO use em producao.");
        }

        var keyId = string.IsNullOrWhiteSpace(options.KeyId) ? ComputeKeyId(_rsa) : options.KeyId;

        _privateKey = new RsaSecurityKey(_rsa) { KeyId = keyId };

        // Chave somente-publica para distribuicao via JWKS e validacao.
        var publicRsa = RSA.Create();
        publicRsa.ImportParameters(_rsa.ExportParameters(includePrivateParameters: false));
        _publicKey = new RsaSecurityKey(publicRsa) { KeyId = keyId };
    }

    public string KeyId => _privateKey.KeyId!;

    /// <summary>Credenciais de assinatura (chave privada, RS256).</summary>
    public SigningCredentials SigningCredentials => new(_privateKey, SecurityAlgorithms.RsaSha256);

    /// <summary>Chave publica para validacao de assinatura.</summary>
    public RsaSecurityKey PublicSecurityKey => _publicKey;

    /// <summary>Chave publica no formato JWK (para o endpoint /.well-known/jwks.json).</summary>
    public JsonWebKey GetPublicJsonWebKey()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_publicKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        return jwk;
    }

    private static string ComputeKeyId(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(spki);
        return Base64UrlEncoder.Encode(hash)[..16];
    }

    public void Dispose() => _rsa.Dispose();
}
