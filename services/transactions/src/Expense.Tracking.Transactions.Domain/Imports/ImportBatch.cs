using Expense.Tracking.Transactions.Domain.CardPayments;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.Imports;

public enum ImportFormat
{
    Ofx = 0,
    Csv = 1
}

public enum ImportBatchStatus
{
    Pending = 0,
    Confirmed = 1,
    Discarded = 2,

    /// <summary>Confirmado e depois desfeito (IMP-9): os lancamentos criados por ele foram excluidos.</summary>
    Undone = 3
}

/// <summary>Situacao de uma linha na previa (ADR-0004, decisoes 3 e 6).</summary>
public enum ImportLineStatus
{
    /// <summary>Linha nova, incluida por padrao.</summary>
    New = 0,

    /// <summary>Ja existe lancamento com a mesma chave externa na conta: fica de fora.</summary>
    Duplicate = 1,

    /// <summary>Data, valor ou descricao invalidos no arquivo: nao pode ser incluida.</summary>
    Invalid = 2,

    /// <summary>Credito na fatura do cartao (pagamento da fatura, estorno): fora por padrao.</summary>
    ExcludedByDefault = 3,

    /// <summary>Virou lancamento na confirmacao (IMP-5).</summary>
    Imported = 4
}

/// <summary>
/// IMP-5: o que o usuario decidiu para uma linha na previa. Campo nulo mantem o que a previa ja
/// tinha (incluida por padrao, meio do lote, categoria sugerida). INV-11: <see cref="InvoicePayment"/>
/// troca a linha entre pagamento de fatura e lancamento comum; cartao e mes escolhem a fatura.
/// </summary>
public sealed record ImportLineDecision(
    Guid LineId, bool? Included, string? Category, PaymentMethod? PaymentMethod,
    bool? InvoicePayment = null, Guid? InvoiceCardId = null, DateOnly? InvoiceDueMonth = null);

/// <summary>O que a confirmacao criou: lancamentos e (INV-11) pagamentos de fatura.</summary>
public sealed record ImportConfirmation(IReadOnlyList<Transaction> Transactions, IReadOnlyList<CardPayment> Payments);

/// <summary>
/// Lote de importacao (IMP-4, ADR-0004 decisao 2): o que foi lido de um arquivo, guardado como
/// previa ate ser confirmado ou descartado. O arquivo original nao e guardado, so as linhas.
/// </summary>
public sealed class ImportBatch
{
    public const int MaxLines = 5_000;

    private static readonly IReadOnlyDictionary<string, string> EmptyHistory = new Dictionary<string, string>();
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(7);

    private readonly List<ImportLine> _lines = [];

    private ImportBatch(
        Guid id, Guid ownerUserId, Guid accountId, Guid? creditCardId, ImportFormat format, StatementKind kind,
        string currency, PaymentMethod defaultPaymentMethod, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        AccountId = accountId;
        CreditCardId = creditCardId;
        Format = format;
        Kind = kind;
        Currency = currency;
        DefaultPaymentMethod = defaultPaymentMethod;
        Status = ImportBatchStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc + PendingLifetime;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? CreditCardId { get; private set; }

    public ImportFormat Format { get; private set; }

    public StatementKind Kind { get; private set; }

    public string Currency { get; private set; }

    public PaymentMethod DefaultPaymentMethod { get; private set; }

    public ImportBatchStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    public DateTimeOffset? UndoneAtUtc { get; private set; }

    public IReadOnlyList<ImportLine> Lines => _lines;

    /// <summary>
    /// Monta a previa (meio de pagamento por <see cref="ResolvePaymentMethod"/>).
    /// <paramref name="existingExternalIds"/> sao as chaves ja lancadas na conta, que viram
    /// <see cref="ImportLineStatus.Duplicate"/>. <paramref name="categoryHistory"/> (IMP-6) e a
    /// ultima categoria usada por chave de descricao (<see cref="Transaction.DescriptionKeyOf"/>):
    /// vira a categoria sugerida das linhas que podem entrar. <paramref name="accountCards"/> (INV-11)
    /// sao os cartoes ativos da conta: com um so, a linha de pagamento de fatura ja vem com ele.
    /// </summary>
    public static Result<ImportBatch> Create(
        Guid ownerUserId, Guid accountId, Guid? creditCardId, PaymentMethod? defaultPaymentMethod,
        ImportFormat format, ParsedStatement statement, IReadOnlySet<string> existingExternalIds,
        DateTimeOffset nowUtc, IReadOnlyDictionary<string, string>? categoryHistory = null,
        IReadOnlyList<InvoiceCardHint>? accountCards = null)
    {
        if (ownerUserId == Guid.Empty || accountId == Guid.Empty)
        {
            return Result.Failure<ImportBatch>(Error.Validation("import.owner_account_required", "Dono e conta sao obrigatorios."));
        }

        if (statement.Lines.Count > MaxLines)
        {
            return Result.Failure<ImportBatch>(ImportErrors.TooManyLines);
        }

        var paymentMethod = ResolvePaymentMethod(statement.Kind, creditCardId, defaultPaymentMethod);
        if (paymentMethod.IsFailure)
        {
            return Result.Failure<ImportBatch>(paymentMethod.Error);
        }

        var batch = new ImportBatch(
            Guid.NewGuid(), ownerUserId, accountId, creditCardId, format, statement.Kind,
            statement.Currency, paymentMethod.Value, nowUtc);

        foreach (var line in statement.Lines)
        {
            batch._lines.Add(ImportLine.From(
                batch.Id, line, statement.Kind, paymentMethod.Value, existingExternalIds,
                categoryHistory ?? EmptyHistory, accountCards is [var onlyCard] ? onlyCard : null));
        }

        return batch;
    }

    /// <summary>
    /// Meio de pagamento das linhas (ADR-0004, decisao 5): fatura exige cartao e usa
    /// <see cref="PaymentMethod.CreditCard"/>; extrato nao aceita cartao e exige um meio padrao.
    /// </summary>
    public static Result<PaymentMethod> ResolvePaymentMethod(
        StatementKind kind, Guid? creditCardId, PaymentMethod? defaultPaymentMethod)
    {
        if (kind == StatementKind.CreditCard)
        {
            return creditCardId is null
                ? Result.Failure<PaymentMethod>(ImportErrors.CardRequired)
                : PaymentMethod.CreditCard;
        }

        if (creditCardId is not null)
        {
            return Result.Failure<PaymentMethod>(ImportErrors.CardNotAllowed);
        }

        return defaultPaymentMethod switch
        {
            null => Result.Failure<PaymentMethod>(ImportErrors.PaymentMethodRequired),
            PaymentMethod.CreditCard => Result.Failure<PaymentMethod>(ImportErrors.CreditCardOnBankStatement),
            { } method => method,
        };
    }

    /// <summary>
    /// IMP-5: aplica as decisoes do usuario e transforma as linhas incluidas em lancamentos, com a
    /// chave externa e o lote de origem. Toda linha incluida precisa de categoria; se faltar em
    /// alguma, o erro traz os numeros das linhas em <c>Metadata["lines"]</c> e nada e confirmado.
    /// <paramref name="alreadyLaunched"/> sao as chaves lancadas na conta depois da previa (outra
    /// importacao): essas linhas viram duplicadas em vez de lancamento. INV-11: linha incluida como
    /// pagamento de fatura vira <see cref="CardPayment"/> (sem categoria; cartao e mes obrigatorios).
    /// </summary>
    public Result<ImportConfirmation> Confirm(
        IReadOnlyCollection<ImportLineDecision> decisions, IReadOnlySet<string> alreadyLaunched, DateTimeOffset nowUtc)
    {
        if (Status != ImportBatchStatus.Pending)
        {
            return Result.Failure<ImportConfirmation>(ImportErrors.NotPending);
        }

        var lines = _lines.ToDictionary(l => l.Id);
        foreach (var decision in decisions)
        {
            if (!lines.TryGetValue(decision.LineId, out var line))
            {
                return Result.Failure<ImportConfirmation>(ImportErrors.LineNotFound);
            }

            var applied = line.Apply(decision, Kind);
            if (applied.IsFailure)
            {
                return Result.Failure<ImportConfirmation>(applied.Error);
            }
        }

        foreach (var line in _lines.Where(l => l.Included && alreadyLaunched.Contains(l.ExternalId!)))
        {
            line.MarkDuplicate();
        }

        var withoutCategory = _lines
            .Where(l => l.Included && !l.InvoicePayment && l.Category is null)
            .Select(l => l.Number).Order().ToList();
        if (withoutCategory.Count > 0)
        {
            return Result.Failure<ImportConfirmation>(ImportErrors.CategoriesRequired(withoutCategory));
        }

        var withoutInvoice = _lines
            .Where(l => l.Included && l.InvoicePayment && (l.InvoiceCardId is null || l.InvoiceDueMonth is null))
            .Select(l => l.Number).Order().ToList();
        if (withoutInvoice.Count > 0)
        {
            return Result.Failure<ImportConfirmation>(ImportErrors.InvoicesRequired(withoutInvoice));
        }

        var created = new List<Transaction>();
        var payments = new List<CardPayment>();
        foreach (var line in _lines.Where(l => l.Included && l.InvoicePayment).OrderBy(l => l.Number))
        {
            var payment = CardPayment.CreateImported(
                OwnerUserId, line.InvoiceCardId!.Value, line.InvoiceDueMonth!.Value, AccountId, line.Amount!.Value,
                Currency, line.OccurredOn!.Value, line.ExternalId!, Id, nowUtc);
            if (payment.IsFailure)
            {
                return Result.Failure<ImportConfirmation>(payment.Error with
                {
                    Metadata = new Dictionary<string, object?> { ["lines"] = new[] { line.Number } },
                });
            }

            line.MarkImportedAsPayment(payment.Value.Id);
            payments.Add(payment.Value);
        }

        foreach (var line in _lines.Where(l => l.Included && !l.InvoicePayment).OrderBy(l => l.Number))
        {
            var transaction = Transaction.CreateImported(
                OwnerUserId, AccountId, line.Type!.Value, line.PaymentMethod!.Value,
                line.PaymentMethod == PaymentMethod.CreditCard ? CreditCardId : null,
                line.Description, line.Category!, line.Amount!.Value, Currency, line.OccurredOn!.Value,
                line.ExternalId!, Id, nowUtc);
            if (transaction.IsFailure)
            {
                return Result.Failure<ImportConfirmation>(transaction.Error with
                {
                    Metadata = new Dictionary<string, object?> { ["lines"] = new[] { line.Number } },
                });
            }

            line.MarkImported(transaction.Value.Id);
            created.Add(transaction.Value);
        }

        Status = ImportBatchStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        return new ImportConfirmation(created, payments);
    }

    /// <summary>
    /// IMP-9: desfaz um lote confirmado. Quem chama exclui os lancamentos que ainda existem com este
    /// lote de origem; as linhas ficam como estavam, como registro do que foi importado. Um lote
    /// desfeito nao volta a ser confirmado: para importar de novo, envia-se o arquivo outra vez.
    /// </summary>
    public Result Undo(DateTimeOffset nowUtc)
    {
        if (Status != ImportBatchStatus.Confirmed)
        {
            return Result.Failure(ImportErrors.NotConfirmed);
        }

        Status = ImportBatchStatus.Undone;
        UndoneAtUtc = nowUtc;
        return Result.Success();
    }

    public bool IsExpired(DateTimeOffset nowUtc) => Status == ImportBatchStatus.Pending && nowUtc >= ExpiresAtUtc;
}

/// <summary>Uma linha da previa, com o que vai virar lancamento se for incluida.</summary>
public sealed class ImportLine
{
    /// <summary>Tamanho maximo da chave externa: o FITID do OFX vai ate 255 caracteres.</summary>
    public const int ExternalIdMaxLength = 255;

    private ImportLine(Guid id, Guid batchId, int number)
    {
        Id = id;
        BatchId = batchId;
        Number = number;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid BatchId { get; private set; }

    /// <summary>Lancamento no OFX (ordem) ou linha no CSV (como o usuario ve no arquivo).</summary>
    public int Number { get; private set; }

    public DateOnly? OccurredOn { get; private set; }

    /// <summary>Valor sempre positivo, como no lancamento; o sentido esta em <see cref="Type"/>.</summary>
    public decimal? Amount { get; private set; }

    public TransactionType? Type { get; private set; }

    public string Description { get; private set; }

    public string? ExternalId { get; private set; }

    public PaymentMethod? PaymentMethod { get; private set; }

    /// <summary>Categoria sugerida ou escolhida; obrigatoria para confirmar uma linha incluida (IMP-5).</summary>
    public string? Category { get; private set; }

    public ImportLineStatus Status { get; private set; }

    public bool Included { get; private set; }

    public string? Problem { get; private set; }

    /// <summary>Lancamento criado por esta linha na confirmacao (IMP-5).</summary>
    public Guid? TransactionId { get; private set; }

    /// <summary>
    /// INV-11: a linha e (ou o usuario marcou como) pagamento de fatura: na confirmacao vira
    /// pagamento, nao gasto. So em extrato de conta e em saida.
    /// </summary>
    public bool InvoicePayment { get; private set; }

    /// <summary>INV-11: cartao cuja fatura a linha paga (sugerido quando a conta tem um so).</summary>
    public Guid? InvoiceCardId { get; private set; }

    /// <summary>INV-11: fatura paga, pelo mes de vencimento (dia 1).</summary>
    public DateOnly? InvoiceDueMonth { get; private set; }

    /// <summary>INV-11: pagamento de fatura criado por esta linha na confirmacao.</summary>
    public Guid? CardPaymentId { get; private set; }

    /// <summary>So linha nova ou fora por padrao pode entrar; duplicada e invalida, nao.</summary>
    public bool CanBeIncluded => Status is ImportLineStatus.New or ImportLineStatus.ExcludedByDefault;

    internal Result Apply(ImportLineDecision decision, StatementKind kind)
    {
        if (decision.Included is { } included)
        {
            if (included && !CanBeIncluded)
            {
                return Result.Failure(ImportErrors.LineNotIncludable(Number));
            }

            Included = included;
        }

        if (decision.Category is not null)
        {
            var category = decision.Category.Trim();
            if (category.Length > Transaction.CategoryMaxLength)
            {
                return Result.Failure(Error.Validation(
                    "transaction.category_too_long",
                    $"A categoria deve ter no maximo {Transaction.CategoryMaxLength} caracteres (linha {Number})."));
            }

            Category = category.Length == 0 ? null : category;
        }

        if (decision.InvoicePayment is { } invoicePayment)
        {
            // Pagamento de fatura so sai de conta: extrato de conta e saida (ADR-0005, decisao 10).
            if (invoicePayment && (kind != StatementKind.BankAccount || Type != TransactionType.Expense))
            {
                return Result.Failure(ImportErrors.LineInvoicePaymentInvalid(Number));
            }

            InvoicePayment = invoicePayment;
        }

        if (decision.InvoiceCardId is { } cardId)
        {
            InvoiceCardId = cardId;
        }

        if (decision.InvoiceDueMonth is { } dueMonth)
        {
            InvoiceDueMonth = new DateOnly(dueMonth.Year, dueMonth.Month, 1);
        }

        if (decision.PaymentMethod is { } method)
        {
            // Fatura e sempre cartao de credito; extrato nunca (ADR-0004, decisao 5).
            var valid = kind == StatementKind.CreditCard
                ? method == Transactions.PaymentMethod.CreditCard
                : method != Transactions.PaymentMethod.CreditCard;
            if (!valid)
            {
                return Result.Failure(ImportErrors.LinePaymentMethodInvalid(Number));
            }

            PaymentMethod = method;
        }

        return Result.Success();
    }

    internal void MarkDuplicate()
    {
        Status = ImportLineStatus.Duplicate;
        Included = false;
    }

    internal void MarkImported(Guid transactionId)
    {
        Status = ImportLineStatus.Imported;
        TransactionId = transactionId;
    }

    internal void MarkImportedAsPayment(Guid cardPaymentId)
    {
        Status = ImportLineStatus.Imported;
        CardPaymentId = cardPaymentId;
    }

    internal static ImportLine From(
        Guid batchId, StatementLine source, StatementKind kind, PaymentMethod paymentMethod,
        IReadOnlySet<string> existingExternalIds, IReadOnlyDictionary<string, string> categoryHistory,
        InvoiceCardHint? onlyAccountCard = null)
    {
        var line = new ImportLine(Guid.NewGuid(), batchId, source.Number)
        {
            Description = DescriptionFrom(source.Description),
        };

        if (!source.IsValid)
        {
            line.Status = ImportLineStatus.Invalid;
            line.Problem = source.Problem;
            return line;
        }

        if (source.ExternalId is { Length: > ExternalIdMaxLength })
        {
            line.Status = ImportLineStatus.Invalid;
            line.Problem = "Identificador do lancamento no arquivo e longo demais.";
            return line;
        }

        var amount = source.Amount!.Value;
        line.OccurredOn = source.Date;
        line.Amount = Math.Abs(amount);
        line.Type = amount < 0 ? TransactionType.Expense : TransactionType.Income;
        line.ExternalId = source.ExternalId;
        // Extrato: pista no memo ("PIX", "BOLETO"...) sugere o meio; sem pista, vale o do lote (IMP-6).
        line.PaymentMethod = kind == StatementKind.BankAccount
            ? PaymentMethodHints.Suggest(source.Description) ?? paymentMethod
            : paymentMethod;

        if (source.ExternalId is not null && existingExternalIds.Contains(source.ExternalId))
        {
            line.Status = ImportLineStatus.Duplicate;
        }
        else if (kind == StatementKind.CreditCard && line.Type == TransactionType.Income)
        {
            line.Status = ImportLineStatus.ExcludedByDefault;
        }
        else
        {
            line.Status = ImportLineStatus.New;
            line.Included = true;
        }

        if (line.CanBeIncluded &&
            categoryHistory.TryGetValue(Transaction.DescriptionKeyOf(line.Description), out var category))
        {
            line.Category = category;
        }

        // INV-11: saida de conta com "PAG FATURA" e afins vem sugerida como pagamento de fatura; com
        // um so cartao na conta, ja vem com ele e com a fatura de vencimento mais proximo.
        if (kind == StatementKind.BankAccount && line.Type == TransactionType.Expense && InvoicePaymentHints.Matches(source.Description))
        {
            line.InvoicePayment = true;
            if (onlyAccountCard is not null)
            {
                line.InvoiceCardId = onlyAccountCard.CardId;
                line.InvoiceDueMonth = InvoicePaymentHints.SuggestDueMonth(line.OccurredOn!.Value, onlyAccountCard.DueDay);
            }
        }

        return line;
    }

    /// <summary>O lancamento aceita ate 200 caracteres; memo de banco maior que isso e cortado.</summary>
    public static string DescriptionFrom(string memo) =>
        memo.Length <= Transaction.DescriptionMaxLength ? memo : memo[..Transaction.DescriptionMaxLength].TrimEnd();
}
