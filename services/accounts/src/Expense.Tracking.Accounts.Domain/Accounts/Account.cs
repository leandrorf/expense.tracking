using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Domain.Accounts;

/// <summary>
/// Conta financeira de um usuario (ex.: "Conta Pessoal", "Conta PJ - Minha Empresa").
/// Agrupa gastos por escopo (PF/PJ). Pertence sempre a um unico dono (OwnerUserId).
/// </summary>
public sealed class Account
{
    public const int NameMaxLength = 120;

    private Account(
        Guid id, Guid ownerUserId, string name, AccountScope scope,
        string? taxId, string currency, bool isActive,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        Name = name;
        Scope = scope;
        TaxId = taxId;
        Currency = currency;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; }

    public AccountScope Scope { get; private set; }

    /// <summary>Documento (CPF/CNPJ) em digitos; opcional.</summary>
    public string? TaxId { get; private set; }

    public string Currency { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<Account> Create(
        Guid ownerUserId, string? name, AccountScope scope, string? taxId,
        string? currency, DateTimeOffset nowUtc)
    {
        if (ownerUserId == Guid.Empty)
        {
            return Result.Failure<Account>(Error.Validation("account.owner_required", "Dono da conta e obrigatorio."));
        }

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return Result.Failure<Account>(nameResult.Error);
        }

        var taxIdResult = ValidateTaxId(scope, taxId);
        if (taxIdResult.IsFailure)
        {
            return Result.Failure<Account>(taxIdResult.Error);
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "BRL" : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            return Result.Failure<Account>(Error.Validation("account.currency_invalid", "Moeda deve ter 3 letras (ISO 4217)."));
        }

        return new Account(
            Guid.NewGuid(), ownerUserId, nameResult.Value, scope,
            taxIdResult.Value, normalizedCurrency, isActive: true,
            createdAtUtc: nowUtc, updatedAtUtc: nowUtc);
    }

    public Result Rename(string? name, DateTimeOffset nowUtc)
    {
        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult;
        }

        Name = nameResult.Value;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<string>(Error.Validation("account.name_required", "Nome da conta e obrigatorio."));
        }

        var trimmed = name.Trim();
        return trimmed.Length > NameMaxLength
            ? Result.Failure<string>(Error.Validation("account.name_too_long", $"Nome deve ter no maximo {NameMaxLength} caracteres."))
            : trimmed;
    }

    private static Result<string?> ValidateTaxId(AccountScope scope, string? taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            return Result.Success<string?>(null);
        }

        var parsed = Accounts.TaxId.Create(taxId);
        if (parsed.IsFailure)
        {
            return Result.Failure<string?>(parsed.Error);
        }

        var expected = scope == AccountScope.Business ? TaxIdKind.Cnpj : TaxIdKind.Cpf;
        if (parsed.Value.Kind != expected)
        {
            var msg = scope == AccountScope.Business
                ? "Conta empresarial (PJ) exige CNPJ."
                : "Conta pessoal (PF) exige CPF.";
            return Result.Failure<string?>(Error.Validation("account.taxid_scope_mismatch", msg));
        }

        return Result.Success<string?>(parsed.Value.Value);
    }
}
