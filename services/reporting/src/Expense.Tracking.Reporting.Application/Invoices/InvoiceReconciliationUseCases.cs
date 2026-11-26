using System.Globalization;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Invoices;

namespace Expense.Tracking.Reporting.Application.Invoices;

/// <summary>RCN-2 (ADR-0011, decisao 7): conferencias guardadas das faturas.</summary>
public interface IInvoiceReconciliationStore
{
    Task<InvoiceReconciliation?> FindAsync(Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceReconciliation>> ListAsync(Guid ownerUserId, Guid cardId, CancellationToken cancellationToken);

    void Add(InvoiceReconciliation reconciliation);

    void Remove(InvoiceReconciliation reconciliation);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>RCN-4: uma linha do lote de importacao como o transactions a devolve (so o que a conciliacao usa).</summary>
public sealed record ImportLineSnapshot(
    Guid Id, int Number, string Status, DateOnly? OccurredOn, string? Type, decimal? Amount, string Description,
    Guid? TransactionId, Guid? DuplicateOf);

/// <summary>RCN-4: o lote de importacao (fatura do banco) lido no transactions com o token do usuario.</summary>
public sealed record ImportBatchSnapshot(
    Guid Id, string Kind, Guid? CreditCardId, decimal? StatedTotal, DateOnly? StatementStart, DateOnly? StatementEnd,
    IReadOnlyList<ImportLineSnapshot> Lines);

/// <summary>RCN-4 (ADR-0011, decisao 1): le o lote no transactions; nulo = nao existe ou nao e do usuario.</summary>
public interface IImportBatchReader
{
    Task<ImportBatchSnapshot?> GetAsync(Guid batchId, CancellationToken cancellationToken);
}

/// <summary>RCN-4: uma entrada do resultado. Linha do banco e item do app, um dos dois ou os dois (casados).</summary>
public sealed record ReconciliationEntryDto(
    string Group,
    string? Rule,
    Guid? LineId,
    int? LineNumber,
    DateOnly? BankDate,
    decimal? BankAmount,
    string? BankDescription,
    Guid? TransactionId,
    DateOnly? AppDate,
    decimal? AppAmount,
    string? AppDescription,
    int? Installment,
    int? Installments,
    decimal Explains,
    bool Charge = false);

/// <summary>RCN-4: quanto da diferenca cada grupo explica; <see cref="Unexplained"/> e o resto (total declarado pelo banco).</summary>
public sealed record ReconciliationExplanationDto(decimal OnlyInBank, decimal OnlyInApp, decimal DifferentAmount, decimal Unexplained);

/// <summary>
/// RCN-4 (ADR-0011, decisoes 2 a 6): a conciliacao com o arquivo. <see cref="FileTotal"/> e o total do banco pelo
/// arquivo (o declarado no OFX ou a soma das linhas); <see cref="SuggestedDueMonth"/> e a fatura que o arquivo
/// parece ser.
/// </summary>
public sealed record ReconciliationDetailDto(
    Guid BatchId,
    decimal FileTotal,
    decimal? StatedTotal,
    decimal LinesTotal,
    decimal Difference,
    string? SuggestedDueMonth,
    ReconciliationExplanationDto Explanation,
    IReadOnlyList<ReconciliationEntryDto> Entries);

/// <summary>A conferencia guardada: total do banco, total do app naquele momento, a diferenca aceita e a data.</summary>
/// <param name="ChangedSince">RCN-8: o total do app agora e outro que o da conferencia (algo entrou ou saiu do ciclo).</param>
public sealed record InvoiceReconciliationDto(
    decimal BankTotal, decimal AppTotal, decimal Difference, DateTimeOffset ReconciledAtUtc, bool ChangedSince = false)
{
    /// <param name="currentAppTotal">Total da fatura do app agora.</param>
    public static InvoiceReconciliationDto? From(InvoiceReconciliation? r, decimal currentAppTotal) =>
        r is null ? null : new(r.BankTotal, r.AppTotal, r.Difference, r.ReconciledAtUtc, currentAppTotal != r.AppTotal);
}

/// <summary>
/// RCN-2: a fatura vista para conferir. <see cref="AppTotal"/> e o total calculado agora; <see cref="BankTotal"/> e
/// <see cref="Difference"/> (banco - app agora) vem da conferencia guardada, quando ha.
/// </summary>
public sealed record InvoiceReconciliationViewDto(
    string DueMonth, decimal AppTotal, decimal? BankTotal, decimal? Difference, InvoiceReconciliationDto? Reconciliation,
    ReconciliationDetailDto? Detail = null);

/// <summary>
/// RCN-2 (ADR-0011, decisoes 7 e 8): ver, marcar e desfazer a conferencia de uma fatura pelo total do banco. O
/// total do app e o da fatura calculada (<see cref="CardInvoiceSource"/>); mes sem compra = 0.
/// </summary>
public sealed class InvoiceReconciliationHandler(
    ICardDirectory cards,
    IProjectionQueries projection,
    IInvoiceReconciliationStore reconciliations,
    InvoiceOptions options,
    TimeProvider timeProvider,
    IImportBatchReader? batches = null)
{
    public static readonly Error NotFound = Error.NotFound("reconciliation.not_found", "Esta fatura nao foi conferida.");

    public static readonly Error BatchNotFound = Error.NotFound(
        "reconciliation.batch_not_found", "Importacao nao encontrada para o usuario.");

    public static readonly Error BatchCardMismatch = Error.Validation(
        "reconciliation.batch_card_mismatch", "A importacao nao e uma fatura deste cartao.");

    public async Task<Result<InvoiceReconciliationViewDto>> GetAsync(
        Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken, Guid? batchId = null)
    {
        var invoice = await InvoiceAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (invoice.IsFailure)
        {
            return Result.Failure<InvoiceReconciliationViewDto>(invoice.Error);
        }

        var (card, items, total) = invoice.Value;
        var stored = await reconciliations.FindAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (batchId is not { } id)
        {
            return View(dueMonth, total, stored);
        }

        // RCN-4 (ADR-0011, decisao 1): o detalhe vem do lote, lido no transactions com o token do usuario.
        var batch = batches is null ? null : await batches.GetAsync(id, cancellationToken);
        if (batch is null)
        {
            return Result.Failure<InvoiceReconciliationViewDto>(BatchNotFound);
        }

        if (batch.Kind != "CreditCard" || batch.CreditCardId != cardId)
        {
            return Result.Failure<InvoiceReconciliationViewDto>(BatchCardMismatch);
        }

        return View(dueMonth, total, stored) with { Detail = Detail(batch, card, items, total) };
    }

    /// <summary>
    /// RCN-4 (ADR-0011, decisoes 2 a 4 e 6): casa as linhas validas do arquivo (sem os pagamentos de fatura) com os
    /// itens da fatura e explica a diferenca por grupo.
    /// </summary>
    private static ReconciliationDetailDto Detail(ImportBatchSnapshot batch, CardInfo card, IReadOnlyList<InvoiceItem> items, decimal appTotal)
    {
        var lines = batch.Lines
            .Where(l => l.Status != "Invalid" && l.OccurredOn is not null && l.Amount is not null)
            .Where(l => !(l.Type == "Income" && InvoiceLineHints.IsPayment(l.Description)))
            .Select(l => new BankLine(l.Id, l.Number, l.OccurredOn!.Value, l.Type == "Income" ? -l.Amount!.Value : l.Amount!.Value,
                l.Description, l.DuplicateOf ?? l.TransactionId))
            .ToList();
        var entries = InvoiceMatcher.Match(lines, items);
        var linesTotal = lines.Sum(l => l.Amount);
        var fileTotal = batch.StatedTotal ?? linesTotal;
        var explained = (
            OnlyInBank: entries.Where(e => e.Group == ReconciliationGroup.OnlyInBank).Sum(e => e.Explains),
            OnlyInApp: entries.Where(e => e.Group == ReconciliationGroup.OnlyInApp).Sum(e => e.Explains),
            Different: entries.Where(e => e.Group == ReconciliationGroup.DifferentAmount).Sum(e => e.Explains));
        var difference = fileTotal - appTotal;

        return new ReconciliationDetailDto(
            batch.Id,
            fileTotal,
            batch.StatedTotal,
            linesTotal,
            difference,
            SuggestedDueMonth(batch, lines, new CardCycle(card.ClosingDay, card.DueDay)),
            new ReconciliationExplanationDto(explained.OnlyInBank, explained.OnlyInApp, explained.Different,
                difference - explained.OnlyInBank - explained.OnlyInApp - explained.Different),
            entries
                .OrderBy(e => e.Group)
                .ThenBy(e => e.Line?.Date ?? e.Item!.PurchaseDate)
                .ThenBy(e => e.Line?.Number ?? int.MaxValue)
                .Select(e => new ReconciliationEntryDto(
                    e.Group.ToString(), e.Rule?.ToString(), e.Line?.LineId, e.Line?.Number, e.Line?.Date, e.Line?.Amount,
                    e.Line?.Description, e.Item?.TransactionId, e.Item?.PurchaseDate, e.Item?.Amount, e.Item?.Description,
                    e.Item?.Installment, e.Item?.Installments, e.Explains, e.IsCharge))
                .ToList());
    }

    /// <summary>
    /// ADR-0011, decisao 6: a fatura do fechamento declarado no OFX (<c>DTEND</c>); sem ele, a que recebe mais
    /// compras das linhas que nao sao parcela.
    /// </summary>
    private static string? SuggestedDueMonth(ImportBatchSnapshot batch, IReadOnlyList<BankLine> lines, CardCycle cycle)
    {
        DateOnly? month = batch.StatementEnd is { } end
            ? cycle.DueMonthOf(end)
            : lines.Where(l => l.Amount > 0 && InvoiceMatcher.InstallmentOf(l.Description) is null)
                .GroupBy(l => cycle.DueMonthOf(l.Date))
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .Select(g => (DateOnly?)g.Key)
                .FirstOrDefault();
        return month?.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    public async Task<Result<InvoiceReconciliationViewDto>> ReconcileAsync(
        Guid ownerUserId, Guid cardId, DateOnly dueMonth, decimal bankTotal, CancellationToken cancellationToken)
    {
        var invoice = await InvoiceAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (invoice.IsFailure)
        {
            return Result.Failure<InvoiceReconciliationViewDto>(invoice.Error);
        }

        var total = Result.Success(invoice.Value.Total);

        var now = timeProvider.GetUtcNow();
        var stored = await reconciliations.FindAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (stored is null)
        {
            var created = InvoiceReconciliation.Create(ownerUserId, cardId, dueMonth, bankTotal, total.Value, now);
            if (created.IsFailure)
            {
                return Result.Failure<InvoiceReconciliationViewDto>(created.Error);
            }

            stored = created.Value;
            reconciliations.Add(stored);
        }
        else
        {
            var updated = stored.Update(bankTotal, total.Value, now);
            if (updated.IsFailure)
            {
                return Result.Failure<InvoiceReconciliationViewDto>(updated.Error);
            }
        }

        await reconciliations.SaveChangesAsync(cancellationToken);
        return View(dueMonth, total.Value, stored);
    }

    public async Task<Result> UndoAsync(Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken)
    {
        if (await cards.FindAsync(cardId, cancellationToken) is null)
        {
            return Result.Failure(GetCardInvoicesHandler.CardNotFound);
        }

        var stored = await reconciliations.FindAsync(ownerUserId, cardId, dueMonth, cancellationToken);
        if (stored is null)
        {
            return Result.Failure(NotFound);
        }

        reconciliations.Remove(stored);
        await reconciliations.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>A fatura calculada do mes: itens e total (cartao que o accounts nao devolve ao dono = 404; sem compra = vazia).</summary>
    private async Task<Result<(CardInfo Card, IReadOnlyList<InvoiceItem> Items, decimal Total)>> InvoiceAsync(
        Guid ownerUserId, Guid cardId, DateOnly dueMonth, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(cardId, cancellationToken);
        if (card is null)
        {
            return Result.Failure<(CardInfo, IReadOnlyList<InvoiceItem>, decimal)>(GetCardInvoicesHandler.CardNotFound);
        }

        var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, CardInvoiceSource.Today(timeProvider, options), cancellationToken);
        var invoice = loaded.Invoices.FirstOrDefault(i => i.Period.DueMonth == CardCycle.FirstDayOf(dueMonth));
        return (card, invoice?.Items ?? [], invoice?.Total ?? 0m);
    }

    private static InvoiceReconciliationViewDto View(DateOnly dueMonth, decimal appTotal, InvoiceReconciliation? stored) => new(
        dueMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        appTotal,
        stored?.BankTotal,
        stored is null ? null : stored.BankTotal - appTotal,
        InvoiceReconciliationDto.From(stored, appTotal));
}
