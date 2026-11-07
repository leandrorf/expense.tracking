namespace Expense.Tracking.Identity.Infrastructure.Security;

/// <summary>Configuracao da chave de assinatura dos tokens (secao "OAuth:SigningKey").</summary>
public sealed class OAuthSigningOptions
{
    public const string SectionName = "OAuth:SigningKey";

    /// <summary>Chave privada RSA em PEM (PKCS#8). Se vazia, uma chave efemera e gerada (apenas DEV).</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Identificador da chave (kid). Se vazio, e derivado da chave publica.</summary>
    public string? KeyId { get; set; }
}
