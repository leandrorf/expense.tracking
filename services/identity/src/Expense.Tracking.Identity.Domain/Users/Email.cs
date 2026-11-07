using System.Text.RegularExpressions;
using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Domain.Users;

/// <summary>
/// Value object de e-mail. Normaliza (trim + lowercase) e valida o formato.
/// </summary>
public sealed partial class Email : IEquatable<Email>
{
    public const int MaxLength = 256;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Email>(Error.Validation("email.empty", "O e-mail e obrigatorio."));
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
        {
            return Result.Failure<Email>(Error.Validation("email.too_long", $"O e-mail deve ter no maximo {MaxLength} caracteres."));
        }

        if (!EmailRegex().IsMatch(normalized))
        {
            return Result.Failure<Email>(Error.Validation("email.invalid", "O formato do e-mail e invalido."));
        }

        return new Email(normalized);
    }

    /// <summary>Reconstrucao a partir de um valor ja persistido/confiavel (ex.: EF Core).</summary>
    public static Email FromTrusted(string value) => new(value);

    public bool Equals(Email? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Email other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
