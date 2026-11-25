using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Contracts;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Recurrences;

internal static class OccurrenceErrors
{
    public static readonly Error PeriodInvalid = Error.Validation(
        "recurrence.period_invalid", "Esse periodo nao e uma ocorrencia da recorrencia (antes do inicio, depois do fim ou invalido).");

    public static readonly Error Closed = Error.Conflict(
        "recurrence.occurrence_closed", "Essa ocorrencia ja foi lancada ou pulada.");
}

/// <summary>
/// REC-3: lancar um periodo. Valor, data e observacoes sao opcionais: sem eles, valem os da regra e a
/// data agendada (o usuario informa o real quando difere).
/// </summary>
public sealed record LaunchOccurrenceCommand(
    Guid RecurrenceId, Guid OwnerUserId, string Period, decimal? Amount, DateOnly? OccurredOn, string? Notes);

/// <summary>
/// REC-3 (ADR-0008, decisao 7): lanca uma ocorrencia em aberto (inclusive futura: conta paga
/// adiantado). Nasce um lancamento comum (<see cref="TransactionRegistered"/>) vinculado a regra e ao
/// periodo, e o periodo fecha (<see cref="RecurrenceOccurrenceClosed"/>), tudo no mesmo commit.
/// </summary>
public sealed class LaunchOccurrenceHandler(
    IRecurrenceRepository recurrences,
    ITransactionRepository transactions,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<TransactionDto>> HandleAsync(LaunchOccurrenceCommand command, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(command.RecurrenceId, command.OwnerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<TransactionDto>(RecurrenceErrors.NotFound);
        }

        if (recurrence.ScheduledOn(command.Period) is not { } scheduledOn)
        {
            return Result.Failure<TransactionDto>(OccurrenceErrors.PeriodInvalid);
        }

        if (await recurrences.FindOccurrenceAsync(recurrence.Id, command.Period, cancellationToken) is not null)
        {
            return Result.Failure<TransactionDto>(OccurrenceErrors.Closed);
        }

        var now = timeProvider.GetUtcNow();
        var created = Transaction.Create(
            recurrence.OwnerUserId, recurrence.AccountId, recurrence.Type, recurrence.PaymentMethod, recurrence.CreditCardId,
            recurrence.Description, recurrence.Category, command.Amount ?? recurrence.AmountFor(command.Period), recurrence.Currency,
            command.OccurredOn ?? scheduledOn, command.Notes ?? recurrence.Notes, now);
        if (created.IsFailure)
        {
            return Result.Failure<TransactionDto>(created.Error);
        }

        // Mesmas conferencias da criacao de um lancamento: a conta traz o escopo do evento.
        var accountScope = await accountsClient.FindAccountScopeAsync(recurrence.AccountId, cancellationToken);
        if (accountScope is null)
        {
            return Result.Failure<TransactionDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
        }

        if (recurrence.CreditCardId is { } cardId && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<TransactionDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        var transaction = created.Value;
        transaction.LinkToRecurrence(recurrence.Id, command.Period);
        await transactions.AddAsync(transaction, cancellationToken);
        recurrences.AddOccurrence(RecurrenceOccurrence.Launched(recurrence, command.Period, transaction.Id, now));
        outbox.Add(TransactionRegistered.From(transaction, accountScope));
        outbox.Add(new RecurrenceOccurrenceClosed(
            recurrence.Id, recurrence.OwnerUserId, command.Period, nameof(OccurrenceStatus.Launched), transaction.Id));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            // Outra aba fechou o mesmo periodo entre a leitura e a gravacao (chave regra + periodo).
            return Result.Failure<TransactionDto>(OccurrenceErrors.Closed);
        }

        return TransactionDto.From(transaction);
    }
}

/// <summary>REC-3 (ADR-0008, decisao 7): pula um periodo em aberto; ele sai da previsao e do "a lancar".</summary>
public sealed class SkipOccurrenceHandler(
    IRecurrenceRepository recurrences, IEventOutbox outbox, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid recurrenceId, Guid ownerUserId, string period, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(recurrenceId, ownerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure(RecurrenceErrors.NotFound);
        }

        if (recurrence.ScheduledOn(period) is null)
        {
            return Result.Failure(OccurrenceErrors.PeriodInvalid);
        }

        if (await recurrences.FindOccurrenceAsync(recurrence.Id, period, cancellationToken) is not null)
        {
            return Result.Failure(OccurrenceErrors.Closed);
        }

        recurrences.AddOccurrence(RecurrenceOccurrence.Skipped(recurrence, period, timeProvider.GetUtcNow()));
        outbox.Add(new RecurrenceOccurrenceClosed(recurrence.Id, recurrence.OwnerUserId, period, nameof(OccurrenceStatus.Skipped), null));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Result.Failure(OccurrenceErrors.Closed);
        }

        return Result.Success();
    }
}

/// <summary>
/// REC-3: desfaz o pulo de um periodo (ele volta a ficar em aberto). Periodo lancado nao se reabre por
/// aqui: exclui-se o lancamento.
/// </summary>
public sealed class UnskipOccurrenceHandler(IRecurrenceRepository recurrences, IEventOutbox outbox, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid recurrenceId, Guid ownerUserId, string period, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(recurrenceId, ownerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure(RecurrenceErrors.NotFound);
        }

        var occurrence = await recurrences.FindOccurrenceAsync(recurrence.Id, period, cancellationToken);
        if (occurrence is null || occurrence.Status != OccurrenceStatus.Skipped)
        {
            return Result.Failure(occurrence is null
                ? Error.NotFound("recurrence.occurrence_not_skipped", "Essa ocorrencia nao esta pulada.")
                : Error.Conflict("recurrence.occurrence_launched", "Essa ocorrencia foi lancada: exclua o lancamento para reabri-la."));
        }

        recurrences.RemoveOccurrence(occurrence);
        outbox.Add(new RecurrenceOccurrenceReopened(recurrence.Id, recurrence.OwnerUserId, period));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>REC-3: uma ocorrencia que ja devia ter acontecido e nao foi lancada nem pulada.</summary>
public sealed record DueOccurrenceDto(
    Guid RecurrenceId,
    string Period,
    DateOnly ScheduledOn,
    Guid AccountId,
    string Type,
    string PaymentMethod,
    Guid? CreditCardId,
    string Description,
    string Category,
    decimal Amount,
    string Currency);

/// <summary>
/// REC-3 (ADR-0008, decisao 8): "A lancar" = periodos em aberto com data agendada antes de hoje, nos
/// ultimos <see cref="WindowDays"/> dias, das mais antigas para as mais novas. Nao entram na previsao.
/// </summary>
public sealed class ListDueOccurrencesHandler(IRecurrenceRepository recurrences, LocalCalendar calendar, TimeProvider timeProvider)
{
    public const int WindowDays = 60;

    public async Task<IReadOnlyList<DueOccurrenceDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var today = calendar.Today(timeProvider);
        var rules = await recurrences.ListAsync(ownerUserId, cancellationToken);
        var closed = await recurrences.ListClosedPeriodsAsync(ownerUserId, rules.Select(r => r.Id).ToArray(), cancellationToken);
        return rules
            .SelectMany(r => r.Occurrences(today.AddDays(-WindowDays), today.AddDays(-1))
                .Where(o => !(closed.GetValueOrDefault(r.Id)?.Contains(o.Period) ?? false))
                .Select(o => new DueOccurrenceDto(r.Id, o.Period, o.ScheduledOn, r.AccountId, r.Type.ToString(),
                    r.PaymentMethod.ToString(), r.CreditCardId, r.Description, r.Category, r.AmountFor(o.Period), r.Currency)))
            .OrderBy(d => d.ScheduledOn)
            .ThenBy(d => d.Description, StringComparer.Ordinal)
            .ToList();
    }
}
