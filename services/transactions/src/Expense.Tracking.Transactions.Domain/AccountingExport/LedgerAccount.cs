using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.AccountingExport;

/// <summary>
/// CTB-5 (ADR-0006, decisao 7): conta do plano de contas do contador associada a uma categoria,
/// por usuario. O codigo sai na coluna "Conta contabil" da exportacao; o nome e so para a tela.
/// A categoria e comparada exatamente como foi digitada, como o Resumo agrupa.
/// </summary>
public sealed class LedgerAccount
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;
    public const int MaxPerUser = 500;

    private LedgerAccount(Guid id, Guid ownerUserId, string category, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        Category = category;
        Code = string.Empty;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Category { get; private set; }

    /// <summary>Codigo da conta no plano do contador, ex.: "3.1.02".</summary>
    public string Code { get; private set; }

    public string? Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<LedgerAccount> Create(Guid ownerUserId, string? category, string? code, string? name, DateTimeOffset nowUtc)
    {
        var validCategory = NormalizeCategory(category);
        if (validCategory.IsFailure)
        {
            return Result.Failure<LedgerAccount>(validCategory.Error);
        }

        var account = new LedgerAccount(Guid.NewGuid(), ownerUserId, validCategory.Value, nowUtc);
        var changed = account.Change(code, name, nowUtc);
        return changed.IsFailure ? Result.Failure<LedgerAccount>(changed.Error) : account;
    }

    /// <summary>Associar de novo a mesma categoria troca codigo e nome.</summary>
    public Result Change(string? code, string? name, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation("ledger_account.code_required", "O codigo da conta contabil e obrigatorio."));
        }

        var trimmedCode = code.Trim();
        if (trimmedCode.Length > CodeMaxLength)
        {
            return Result.Failure(Error.Validation(
                "ledger_account.code_too_long", $"O codigo da conta contabil deve ter no maximo {CodeMaxLength} caracteres."));
        }

        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmedName?.Length > NameMaxLength)
        {
            return Result.Failure(Error.Validation(
                "ledger_account.name_too_long", $"O nome da conta contabil deve ter no maximo {NameMaxLength} caracteres."));
        }

        Code = trimmedCode;
        Name = trimmedName;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>A categoria como o lancamento a guarda (aparada), para casar com ela.</summary>
    public static Result<string> NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure<string>(Error.Validation("ledger_account.category_required", "A categoria e obrigatoria."));
        }

        var trimmed = category.Trim();
        return trimmed.Length > Transaction.CategoryMaxLength
            ? Result.Failure<string>(Error.Validation(
                "ledger_account.category_too_long", $"A categoria deve ter no maximo {Transaction.CategoryMaxLength} caracteres."))
            : trimmed;
    }
}
