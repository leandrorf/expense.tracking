using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Imports;

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

    /// <summary>INV-5 (ADR-0005, decisao 5): parcelas de 1 a 24; acima de 1 so no cartao de credito.</summary>
    public const int MaxInstallments = 24;

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
        DescriptionKey = DescriptionKeyOf(description);
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

    /// <summary>
    /// IMP-6: descricao comparavel (maiusculas, espacos simples), para achar no historico a
    /// categoria de uma linha importada. "Uber *Trip" e "UBER  *TRIP" tem a mesma chave.
    /// </summary>
    public string DescriptionKey { get; private set; } = string.Empty;

    public string Category { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Chave do lancamento no arquivo importado (FITID ou impressao digital; ADR-0004, decisao 3).
    /// Nula para lancamentos digitados. Unica por dono e conta.
    /// </summary>
    public string? ExternalId { get; private set; }

    /// <summary>Lote de importacao que criou o lancamento, para desfazer a importacao (IMP-9).</summary>
    public Guid? ImportBatchId { get; private set; }

    /// <summary>
    /// INV-2 (ADR-0005, decisao 5): numero de parcelas da compra; 1 quando a vista. A fatura
    /// distribui as parcelas; Resumo e orcamentos contam a compra inteira na data dela.
    /// </summary>
    public int Installments { get; private set; } = 1;

    /// <summary>
    /// LNC-3 (ADR-0007, decisao 4): sobe a cada edicao que muda algo; a edicao manda a versao que leu
    /// e e recusada se ela mudou (concorrencia otimista). Comeca em 1.
    /// </summary>
    public int Version { get; private set; } = 1;

    public static Result<Transaction> Create(
        Guid ownerUserId, Guid accountId, TransactionType type, PaymentMethod paymentMethod,
        Guid? creditCardId, string? description, string? category, decimal amount, string? currency,
        DateOnly occurredOn, string? notes, DateTimeOffset nowUtc, int installments = 1)
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

        var categoryResult = ValidateCategory(category);
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

        if (installments is < 1 or > MaxInstallments)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.installments_invalid", $"Parcelas devem ser de 1 a {MaxInstallments}."));
        }

        if (installments > 1 && paymentMethod != PaymentMethod.CreditCard)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.installments_not_allowed", "Parcelamento so e permitido no cartao de credito."));
        }

        // LNC-2 (ADR-0007, decisao 7): receita no cartao e estorno, sempre de uma vez.
        if (installments > 1 && type == TransactionType.Income)
        {
            return Result.Failure<Transaction>(Error.Validation("transaction.installments_not_allowed", "Receita nao e parcelada."));
        }

        var money = moneyResult.Value;

        // Resumo e orcamentos contam o valor inteiro na data; so a fatura divide as parcelas.
        return new Transaction(
            Guid.NewGuid(), ownerUserId, accountId, type, paymentMethod, creditCardId,
            descriptionResult.Value, categoryResult.Value, money.Amount, money.Currency,
            occurredOn, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            nowUtc, nowUtc)
        {
            Installments = installments,
        };
    }

    /// <summary>
    /// IMP-5: lancamento criado pela confirmacao de uma importacao, com a chave externa (unica por
    /// dono e conta) e o lote de origem. Mesmas regras de <see cref="Create"/>.
    /// </summary>
    public static Result<Transaction> CreateImported(
        Guid ownerUserId, Guid accountId, TransactionType type, PaymentMethod paymentMethod,
        Guid? creditCardId, string description, string category, decimal amount, string currency,
        DateOnly occurredOn, string externalId, Guid importBatchId, DateTimeOffset nowUtc)
    {
        var result = Create(ownerUserId, accountId, type, paymentMethod, creditCardId, description, category,
            amount, currency, occurredOn, notes: null, nowUtc);
        if (result.IsFailure)
        {
            return result;
        }

        result.Value.ExternalId = externalId;
        result.Value.ImportBatchId = importBatchId;
        return result;
    }

    /// <summary>
    /// LNC-3 (ADR-0007, decisoes 2, 3 e 6): corrige o lancamento com as regras da criacao. Conta, id e
    /// origem (chave externa e lote) ficam. Devolve <c>true</c> quando algo mudou (a versao sobe e
    /// <see cref="UpdatedAtUtc"/> anda); sem mudanca, nada e tocado.
    /// </summary>
    public Result<bool> Edit(
        TransactionType type, PaymentMethod paymentMethod, Guid? creditCardId, string? description, string? category,
        decimal amount, string? currency, DateOnly occurredOn, string? notes, int installments, DateTimeOffset nowUtc)
    {
        var valid = Create(OwnerUserId, AccountId, type, paymentMethod, creditCardId, description, category,
            amount, currency, occurredOn, notes, nowUtc, installments);
        if (valid.IsFailure)
        {
            return Result.Failure<bool>(valid.Error);
        }

        var next = valid.Value;
        var changed = Type != next.Type || PaymentMethod != next.PaymentMethod || CreditCardId != next.CreditCardId
            || Description != next.Description || Category != next.Category || Amount != next.Amount
            || Currency != next.Currency || OccurredOn != next.OccurredOn || Notes != next.Notes
            || Installments != next.Installments;
        if (!changed)
        {
            return false;
        }

        Type = next.Type;
        PaymentMethod = next.PaymentMethod;
        CreditCardId = next.CreditCardId;
        Description = next.Description;
        DescriptionKey = next.DescriptionKey;
        Category = next.Category;
        Amount = next.Amount;
        Currency = next.Currency;
        OccurredOn = next.OccurredOn;
        Notes = next.Notes;
        Installments = next.Installments;
        UpdatedAtUtc = nowUtc;
        Version++;
        return true;
    }

    /// <summary>
    /// LNC-7: troca so a categoria, com a mesma validacao e o mesmo controle de versao da edicao.
    /// Devolve <c>true</c> quando a categoria mudou.
    /// </summary>
    public Result<bool> Recategorize(string? category, DateTimeOffset nowUtc) =>
        Edit(Type, PaymentMethod, CreditCardId, Description, category, Amount, Currency, OccurredOn, Notes, Installments, nowUtc);

    /// <summary>LNC-7: a categoria como a criacao a gravaria (aparada), ou o erro da regra.</summary>
    public static Result<string> ValidateCategory(string? category) =>
        ValidateRequiredText(category, CategoryMaxLength, "category", "A categoria");

    /// <summary>A mesma normalizacao da impressao digital da importacao (ADR-0004, decisao 3).</summary>
    public static string DescriptionKeyOf(string description) => StatementFingerprint.NormalizeDescription(description);

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
