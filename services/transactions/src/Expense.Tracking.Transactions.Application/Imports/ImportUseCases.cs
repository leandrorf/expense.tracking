using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Contracts;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Recurrences;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using Expense.Tracking.Transactions.Domain.Imports.Ofx;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Imports;

/// <summary>Arquivo enviado, ja em memoria. O conteudo nunca vai para log, trace ou erro (ADR-0004, decisao 8).</summary>
public sealed record UploadedFile(string FileName, ReadOnlyMemory<byte> Content);

public sealed record PreviewImportCommand(
    Guid OwnerUserId,
    Guid AccountId,
    Guid? CreditCardId,
    PaymentMethod? DefaultPaymentMethod,
    UploadedFile File,
    CsvColumnMapping? Mapping);

/// <summary>
/// IMP-4: le o arquivo, valida conta e cartao no accounts uma vez por lote, marca as duplicadas
/// contra os lancamentos da conta e guarda o lote pendente (ADR-0004).
/// </summary>
public sealed class PreviewImportHandler(
    ICsvTableReader csvTableReader,
    IAccountsClient accountsClient,
    ITransactionRepository transactions,
    ICardPaymentRepository payments,
    IImportBatchRepository batches,
    IRecurrenceRepository recurrences,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<ImportBatchDto>> HandleAsync(PreviewImportCommand command, CancellationToken cancellationToken)
    {
        var format = ImportFiles.FormatOf(command.File);
        if (format.IsFailure)
        {
            return Result.Failure<ImportBatchDto>(format.Error);
        }

        var statement = format.Value switch
        {
            ImportFormat.Ofx => WithFingerprints(OfxReader.Read(command.File.Content.Span)),
            _ => ReadCsv(command),
        };
        if (statement.IsFailure)
        {
            return Result.Failure<ImportBatchDto>(statement.Error);
        }

        if (statement.Value.Lines.Count > ImportBatch.MaxLines)
        {
            return Result.Failure<ImportBatchDto>(ImportErrors.TooManyLines);
        }

        // Regras do lote (cartao x extrato, meio padrao) antes de chamar o accounts.
        var paymentMethod = ImportBatch.ResolvePaymentMethod(
            statement.Value.Kind, command.CreditCardId, command.DefaultPaymentMethod);
        if (paymentMethod.IsFailure)
        {
            return Result.Failure<ImportBatchDto>(paymentMethod.Error);
        }

        if (await accountsClient.FindAccountScopeAsync(command.AccountId, cancellationToken) is null)
        {
            return Result.Failure<ImportBatchDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
        }

        if (command.CreditCardId is { } cardId && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<ImportBatchDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        var keys = statement.Value.Lines
            .Where(l => l.ExternalId is not null)
            .Select(l => l.ExternalId!)
            .ToList();
        var existing = await ExternalIds.ExistingAsync(
            transactions, payments, command.OwnerUserId, command.AccountId, keys, cancellationToken);

        // IMP-6: ultima categoria usada para a mesma descricao, em qualquer conta do usuario.
        var descriptionKeys = statement.Value.Lines
            .Where(l => l.IsValid)
            .Select(l => Transaction.DescriptionKeyOf(ImportLine.DescriptionFrom(l.Description)))
            .ToList();
        var categoryHistory = await transactions.FindLatestCategoriesAsync(
            command.OwnerUserId, descriptionKeys, cancellationToken);

        // INV-11: so busca os cartoes da conta se alguma saida do extrato parece pagamento de fatura.
        var accountCards = statement.Value.Kind == StatementKind.BankAccount
            && statement.Value.Lines.Any(l => l.IsValid && l.Amount < 0 && InvoicePaymentHints.Matches(l.Description))
                ? await accountsClient.ListActiveCardsAsync(command.AccountId, cancellationToken)
                : [];

        // REC-9: ocorrencias em aberto das regras desta conta perto das datas do extrato.
        var openOccurrences = await OpenOccurrencesAsync(command.OwnerUserId, command.AccountId, statement.Value, cancellationToken);

        var batch = ImportBatch.Create(
            command.OwnerUserId, command.AccountId, command.CreditCardId, command.DefaultPaymentMethod,
            format.Value, statement.Value, existing, timeProvider.GetUtcNow(), categoryHistory, accountCards, openOccurrences);
        if (batch.IsFailure)
        {
            return Result.Failure<ImportBatchDto>(batch.Error);
        }

        await batches.AddAsync(batch.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ImportBatchDto.From(batch.Value);
    }

    private async Task<IReadOnlyList<RecurrenceCandidate>> OpenOccurrencesAsync(
        Guid ownerUserId, Guid accountId, ParsedStatement statement, CancellationToken cancellationToken)
    {
        var dates = statement.Lines.Where(l => l.IsValid && l.Date is not null).Select(l => l.Date!.Value).ToList();
        if (dates.Count == 0)
        {
            return [];
        }

        var rules = (await recurrences.ListAsync(ownerUserId, cancellationToken)).Where(r => r.AccountId == accountId).ToList();
        if (rules.Count == 0)
        {
            return [];
        }

        var closed = await recurrences.ListClosedPeriodsAsync(ownerUserId, rules.Select(r => r.Id).ToArray(), cancellationToken);
        var (from, to) = (dates.Min().AddDays(-RecurrenceMatcher.DayTolerance), dates.Max().AddDays(RecurrenceMatcher.DayTolerance));
        return rules
            .SelectMany(r => r.Occurrences(from, to)
                .Where(o => !(closed.GetValueOrDefault(r.Id)?.Contains(o.Period) ?? false))
                .Select(o => new RecurrenceCandidate(r.Id, o.Period, o.ScheduledOn, r.Type, r.AmountFor(o.Period), r.Description, r.Category)))
            .ToList();
    }

    /// <summary>OFX sem FITID usa a impressao digital como chave (ADR-0004, decisao 3).</summary>
    private static Result<ParsedStatement> WithFingerprints(Result<ParsedStatement> statement) =>
        statement.IsFailure
            ? statement
            : statement.Value with { Lines = StatementFingerprint.Assign(statement.Value.Lines) };

    private Result<ParsedStatement> ReadCsv(PreviewImportCommand command)
    {
        if (command.Mapping is null)
        {
            return Result.Failure<ParsedStatement>(ImportErrors.MappingRequired);
        }

        var table = csvTableReader.Read(command.File.Content.Span);
        if (table.IsFailure)
        {
            return Result.Failure<ParsedStatement>(table.Error);
        }

        // O CSV nao diz se e fatura: e fatura quando o usuario escolhe o cartao (ADR-0004, decisao 5).
        var kind = command.CreditCardId is null ? StatementKind.BankAccount : StatementKind.CreditCard;
        return CsvStatementParser.Parse(table.Value, command.Mapping, kind);
    }
}

/// <summary>IMP-4/IMP-8: proposta de mapeamento de um CSV, sem guardar nada.</summary>
public sealed class ProposeCsvMappingHandler(ICsvTableReader csvTableReader)
{
    public Result<CsvMappingProposal> Handle(UploadedFile file)
    {
        var format = ImportFiles.FormatOf(file);
        if (format.IsFailure)
        {
            return Result.Failure<CsvMappingProposal>(format.Error);
        }

        if (format.Value != ImportFormat.Csv)
        {
            return Result.Failure<CsvMappingProposal>(
                ImportErrors.FormatInvalid("Proposta de mapeamento e so para CSV; o OFX nao precisa de mapeamento."));
        }

        var table = csvTableReader.Read(file.Content.Span);
        return table.IsFailure
            ? Result.Failure<CsvMappingProposal>(table.Error)
            : CsvMappingDetector.Propose(table.Value);
    }
}

public sealed record ConfirmImportCommand(Guid OwnerUserId, Guid BatchId, IReadOnlyList<ImportLineDecision> Lines);

/// <summary>
/// IMP-5: confirma o lote. Cria os lancamentos das linhas incluidas e um TransactionRegistered por
/// lancamento no outbox, numa gravacao so. Idempotente: lote ja confirmado devolve o mesmo
/// resultado sem criar nada. Se outra operacao ganhar a corrida (o mesmo lote confirmado em
/// paralelo, ou a mesma chave externa importada por outro lote), recarrega e decide de novo: a
/// linha ja lancada volta como duplicada.
/// </summary>
public sealed class ConfirmImportHandler(
    IImportBatchRepository batches,
    ITransactionRepository transactions,
    ICardPaymentRepository payments,
    IRecurrenceRepository recurrences,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    ImportMetrics metrics,
    TimeProvider timeProvider)
{
    private const int MaxAttempts = 3;

    public async Task<Result<ImportBatchDto>> HandleAsync(ConfirmImportCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var batch = await batches.GetAsync(command.BatchId, command.OwnerUserId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (batch is null || batch.IsExpired(now))
            {
                return Result.Failure<ImportBatchDto>(ImportErrors.BatchNotFound);
            }

            if (batch.Status == ImportBatchStatus.Confirmed)
            {
                return ImportBatchDto.From(batch);
            }

            // A conta (ou o cartao) pode ter sumido entre a previa e a confirmacao (ADR-0004, decisao 8).
            var accountScope = await accountsClient.FindAccountScopeAsync(batch.AccountId, cancellationToken);
            if (accountScope is null)
            {
                return Result.Failure<ImportBatchDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
            }

            if (batch.CreditCardId is { } cardId && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
            {
                return Result.Failure<ImportBatchDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
            }

            var keys = batch.Lines.Where(l => l.CanBeIncluded && l.ExternalId is not null).Select(l => l.ExternalId!).ToList();
            var alreadyLaunched = await ExternalIds.ExistingAsync(
                transactions, payments, batch.OwnerUserId, batch.AccountId, keys, cancellationToken);

            var created = batch.Confirm(command.Lines, alreadyLaunched, now);
            if (created.IsFailure)
            {
                return Result.Failure<ImportBatchDto>(created.Error);
            }

            // INV-11: o cartao de cada fatura paga tem de ser do usuario (o accounts filtra pelo token).
            foreach (var invoiceCard in created.Value.Payments.Select(p => p.CreditCardId).Distinct())
            {
                if (!await accountsClient.CreditCardExistsAsync(invoiceCard, cancellationToken))
                {
                    return Result.Failure<ImportBatchDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
                }
            }

            foreach (var transaction in created.Value.Transactions)
            {
                await CloseRecurrenceAsync(transaction, now, cancellationToken);
                await transactions.AddAsync(transaction, cancellationToken);
                outbox.Add(TransactionRegistered.From(transaction, accountScope));
            }

            foreach (var payment in created.Value.Payments)
            {
                await payments.AddAsync(payment, cancellationToken);
                outbox.Add(CardInvoicePaid.From(payment));
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (PersistenceConflictException) when (attempt < MaxAttempts)
            {
                unitOfWork.Reset();
                continue;
            }

            var dto = ImportBatchDto.From(batch);
            metrics.LinesConfirmed(batch.Format, dto.Summary.Imported, dto.Summary.Duplicate);
            return dto;
        }
    }

    /// <summary>
    /// REC-9 (ADR-0008, decisao 11): a linha confirmada como ocorrencia fecha o periodo no mesmo commit.
    /// Se a regra sumiu ou o periodo ja foi fechado por outro caminho desde a previa, o lancamento entra
    /// comum (sem vinculo). Na corrida pela chave (regra, periodo), a nova tentativa cai neste caso.
    /// </summary>
    private async Task CloseRecurrenceAsync(Transaction transaction, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (transaction.RecurrenceId is not { } recurrenceId || transaction.RecurrencePeriod is not { } period)
        {
            return;
        }

        var recurrence = await recurrences.GetAsync(recurrenceId, transaction.OwnerUserId, cancellationToken);
        if (recurrence is null || recurrence.ScheduledOn(period) is null
            || await recurrences.FindOccurrenceAsync(recurrenceId, period, cancellationToken) is not null)
        {
            transaction.UnlinkRecurrence();
            return;
        }

        recurrences.AddOccurrence(RecurrenceOccurrence.Launched(recurrence, period, transaction.Id, now));
        outbox.Add(new RecurrenceOccurrenceClosed(
            recurrenceId, transaction.OwnerUserId, period, nameof(OccurrenceStatus.Launched), transaction.Id));
    }
}

public sealed record UndoImportCommand(Guid OwnerUserId, Guid BatchId);

/// <summary>
/// IMP-9: desfaz um lote confirmado. Exclui os lancamentos criados por ele que ainda existem (o
/// usuario pode ter excluido algum a mao) e grava um TransactionDeleted por lancamento no outbox,
/// na mesma gravacao que marca o lote como desfeito; Resumo e orcamentos voltam pela projecao.
/// Idempotente: lote ja desfeito devolve o mesmo resultado sem excluir nada. Corrida (o lote
/// desfeito em paralelo, ou um lancamento excluido a mao no meio) recarrega e decide de novo.
/// </summary>
public sealed class UndoImportHandler(
    IImportBatchRepository batches,
    ITransactionRepository transactions,
    ICardPaymentRepository payments,
    IRecurrenceRepository recurrences,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    ImportMetrics metrics,
    TimeProvider timeProvider)
{
    private const int MaxAttempts = 3;

    public async Task<Result<ImportBatchDto>> HandleAsync(UndoImportCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var batch = await batches.GetAsync(command.BatchId, command.OwnerUserId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (batch is null || batch.IsExpired(now))
            {
                return Result.Failure<ImportBatchDto>(ImportErrors.BatchNotFound);
            }

            if (batch.Status == ImportBatchStatus.Undone)
            {
                return ImportBatchDto.From(batch);
            }

            var undone = batch.Undo(now);
            if (undone.IsFailure)
            {
                return Result.Failure<ImportBatchDto>(undone.Error);
            }

            var created = await transactions.ListByImportBatchAsync(batch.OwnerUserId, batch.Id, cancellationToken);
            foreach (var transaction in created)
            {
                transactions.Remove(transaction);
                outbox.Add(new TransactionDeleted(transaction.Id, transaction.OwnerUserId));
                // REC-9: o lancamento de uma ocorrencia reabre o periodo, como na exclusao a mao.
                await RecurrenceLinks.ReopenLaunchedAsync(recurrences, outbox, transaction, cancellationToken);
            }

            // INV-11: os pagamentos de fatura criados pelo lote tambem saem; a fatura volta a dever.
            foreach (var payment in await payments.ListByImportBatchAsync(batch.OwnerUserId, batch.Id, cancellationToken))
            {
                payments.Remove(payment);
                outbox.Add(new CardInvoicePaymentDeleted(payment.Id, payment.OwnerUserId, payment.CreditCardId));
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (PersistenceConflictException) when (attempt < MaxAttempts)
            {
                unitOfWork.Reset();
                continue;
            }

            metrics.TransactionsUndone(batch.Format, created.Count);
            return ImportBatchDto.From(batch);
        }
    }
}

public sealed class GetImportHandler(IImportBatchRepository batches, TimeProvider timeProvider)
{
    public async Task<Result<ImportBatchDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var batch = await batches.GetAsync(id, ownerUserId, cancellationToken);
        return batch is null || batch.IsExpired(timeProvider.GetUtcNow())
            ? Result.Failure<ImportBatchDto>(ImportErrors.BatchNotFound)
            : ImportBatchDto.From(batch);
    }
}

/// <summary>
/// INV-11: chaves externas ja usadas na conta, por lancamento ou por pagamento de fatura importado:
/// reimportar o extrato nao paga a mesma fatura duas vezes.
/// </summary>
internal static class ExternalIds
{
    public static async Task<IReadOnlySet<string>> ExistingAsync(
        ITransactionRepository transactions, ICardPaymentRepository payments, Guid ownerUserId, Guid accountId,
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var launched = await transactions.FindExistingExternalIdsAsync(ownerUserId, accountId, keys, cancellationToken);
        var paid = await payments.FindExistingExternalIdsAsync(ownerUserId, accountId, keys, cancellationToken);
        return paid.Count == 0 ? launched : launched.Union(paid).ToHashSet();
    }
}

/// <summary>Formato pelo nome do arquivo e tamanho; o conteudo e conferido por quem le (OFX/CSV).</summary>
internal static class ImportFiles
{
    public static Result<ImportFormat> FormatOf(UploadedFile file)
    {
        if (file.Content.IsEmpty)
        {
            return Result.Failure<ImportFormat>(ImportErrors.FileRequired);
        }

        if (file.Content.Length > ImportBatch.MaxFileBytes)
        {
            return Result.Failure<ImportFormat>(ImportErrors.FileTooLarge);
        }

        return Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".ofx" or ".qfx" => ImportFormat.Ofx,
            ".csv" => ImportFormat.Csv,
            _ => Result.Failure<ImportFormat>(ImportErrors.FormatNotAccepted),
        };
    }
}
