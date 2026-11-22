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

    /// <summary>
    /// INV-13: saldo informado pelo usuario no fim do dia <see cref="OpeningBalanceOn"/> (pode ser
    /// negativo: cheque especial). Nulo = a conta fica fora do saldo projetado da previsao.
    /// </summary>
    public decimal? OpeningBalance { get; private set; }

    /// <summary>INV-13: dia a que o <see cref="OpeningBalance"/> se refere; o que vem depois dele soma.</summary>
    public DateOnly? OpeningBalanceOn { get; private set; }

    /// <summary>Maior saldo aceito, em modulo (cabe com folga no decimal(18,2) da coluna).</summary>
    public const decimal OpeningBalanceMaxAbs = 1_000_000_000_000m;

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

    /// <summary>
    /// INV-13: informa (ou corrige) o saldo da conta no fim de um dia que ja passou ou e hoje. Valor
    /// com ate 2 casas decimais.
    /// </summary>
    public Result SetOpeningBalance(decimal amount, DateOnly on, DateOnly today, DateTimeOffset nowUtc)
    {
        if (decimal.Round(amount, 2) != amount || Math.Abs(amount) >= OpeningBalanceMaxAbs)
        {
            return Result.Failure(Error.Validation(
                "account.opening_balance_invalid", "Informe o saldo em reais, com ate 2 casas decimais."));
        }

        if (on > today)
        {
            return Result.Failure(Error.Validation(
                "account.opening_balance_date_future", "A data do saldo nao pode ser futura: informe o saldo de hoje ou de um dia que ja passou."));
        }

        OpeningBalance = amount;
        OpeningBalanceOn = on;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>INV-13: tira o saldo; a conta sai do saldo projetado.</summary>
    public void ClearOpeningBalance(DateTimeOffset nowUtc)
    {
        OpeningBalance = null;
        OpeningBalanceOn = null;
        UpdatedAtUtc = nowUtc;
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
