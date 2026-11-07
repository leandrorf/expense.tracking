using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Transactions;

/// <summary>
/// Lancamento de gasto/receita. Referencia uma conta (AccountId) e, quando for cartao de
/// credito, um cartao (CreditCardId) — ambos pertencentes ao mesmo dono.
/// </summary>
public sealed class Transaction
{
    public const int DescriptionMaxLength = 200;
    public const int CategoryMaxLength = 60;
    public const int NotesMaxLength = 500;

    private Transaction(
        Guid id, Guid ownerUserId, Guid accountId, TransactionType type, PaymentMethod paymentMethod,
        Guid? creditCardId, string description, string category, decimal amount, string currency,
        DateOnly occurredOn, string? notes, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        AccountId = accountId;
        Type = type;
        PaymentMethod = paymentMethod;
        CreditCardId = creditCardId;
        Description = description;
        Category = category;
        Amount = amount;
        Currency = currency;
        OccurredOn = occurredOn;
        Notes = notes;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public Guid? CreditCardId { get; private set; }

    public string Description { get; private set; }

    public string Category { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<Transaction> Create(
        Guid ownerUserId, Guid accountId, TransactionType type, PaymentMethod paymentMethod,
        Guid? creditCardId, string? description, string? category, decimal amount, string? currency,
        DateOnly occurredOn, string? notes, DateTimeOffset nowUtc)
    {
        if (ownerUserId == Guid.Empty || accountId == Guid.Empty)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.owner_account_required", "Dono e conta sao obrigatorios."));
        }

        var descriptionResult = ValidateRequiredText(description, DescriptionMaxLength, "description", "A descricao");
        if (descriptionResult.IsFailure)
        {
            return Result.Failure<Transaction>(descriptionResult.Error);
        }

        var categoryResult = ValidateRequiredText(category, CategoryMaxLength, "category", "A categoria");
        if (categoryResult.IsFailure)
        {
            return Result.Failure<Transaction>(categoryResult.Error);
        }

        if (!string.IsNullOrEmpty(notes) && notes.Length > NotesMaxLength)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.notes_too_long", $"As observacoes devem ter no maximo {NotesMaxLength} caracteres."));
        }

        var moneyResult = Money.Create(amount, currency);
        if (moneyResult.IsFailure)
        {
            return Result.Failure<Transaction>(moneyResult.Error);
        }

        // Regra: cartao de credito exige CreditCardId; os demais meios nao podem ter cartao.
        if (paymentMethod == PaymentMethod.CreditCard && creditCardId is null)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.card_required", "Pagamento com cartao de credito exige creditCardId."));
        }

        if (paymentMethod != PaymentMethod.CreditCard && creditCardId is not null)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.card_not_allowed", "creditCardId so e permitido quando o meio de pagamento e cartao de credito."));
        }

        var money = moneyResult.Value;

        return new Transaction(
            Guid.NewGuid(), ownerUserId, accountId, type, paymentMethod, creditCardId,
            descriptionResult.Value, categoryResult.Value, money.Amount, money.Currency,
            occurredOn, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            nowUtc, nowUtc);
    }

    public void Delete()
    {
        // Reservado para soft-delete futuro; hoje a remocao e fisica no repositorio.
    }

    private static Result<string> ValidateRequiredText(string? value, int maxLength, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<string>(Error.Validation($"transaction.{field}_required", $"{label} e obrigatoria."));
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? Result.Failure<string>(Error.Validation($"transaction.{field}_too_long", $"{label} deve ter no maximo {maxLength} caracteres."))
            : trimmed;
    }
}
