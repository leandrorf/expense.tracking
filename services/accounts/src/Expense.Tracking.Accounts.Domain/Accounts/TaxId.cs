using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Domain.Accounts;

public enum TaxIdKind
{
    Cpf = 0,
    Cnpj = 1
}

/// <summary>
/// Value object de documento fiscal brasileiro: CPF (11 digitos) ou CNPJ (14 digitos).
/// Guarda apenas os digitos; valida o tamanho (nao valida digitos verificadores aqui).
/// </summary>
public sealed class TaxId : IEquatable<TaxId>
{
    private TaxId(string digits, TaxIdKind kind)
    {
        Value = digits;
        Kind = kind;
    }

    public string Value { get; }

    public TaxIdKind Kind { get; }

    public static Result<TaxId> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result.Failure<TaxId>(Error.Validation("taxid.empty", "O documento e obrigatorio."));
        }

        var digits = new string(input.Where(char.IsDigit).ToArray());

        return digits.Length switch
        {
            11 => new TaxId(digits, TaxIdKind.Cpf),
            14 => new TaxId(digits, TaxIdKind.Cnpj),
            _ => Result.Failure<TaxId>(Error.Validation("taxid.invalid", "Documento invalido: use CPF (11) ou CNPJ (14) digitos."))
        };
    }

    public static TaxId FromTrusted(string digits) =>
        new(digits, digits.Length == 14 ? TaxIdKind.Cnpj : TaxIdKind.Cpf);

    public bool Equals(TaxId? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => obj is TaxId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
