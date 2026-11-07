using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Domain.Users;

/// <summary>
/// Value object que encapsula o hash da senha ja codificado (algoritmo + parametros + salt + hash).
/// O dominio nao conhece o algoritmo; o calculo/verificacao vive num adaptador de infraestrutura
/// (porta IPasswordHasher na camada de aplicacao).
/// </summary>
public sealed class PasswordHash : IEquatable<PasswordHash>
{
    private PasswordHash(string value) => Value = value;

    public string Value { get; }

    public static Result<PasswordHash> Create(string? encodedHash)
    {
        return string.IsNullOrWhiteSpace(encodedHash)
            ? Result.Failure<PasswordHash>(Error.Validation("password_hash.empty", "O hash da senha e obrigatorio."))
            : new PasswordHash(encodedHash);
    }

    /// <summary>Reconstrucao a partir de um valor ja persistido/confiavel (ex.: EF Core).</summary>
    public static PasswordHash FromTrusted(string value) => new(value);

    public bool Equals(PasswordHash? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PasswordHash other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
