using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Application.Recurrences;

public sealed record SetRecurrenceAmountCommand(Guid Id, Guid OwnerUserId, string FromPeriod, decimal Amount, int Version);

/// <summary>
/// RJT-2 (ADR-0010, decisoes 1 e 2): o valor passa a outro a partir de um periodo, sem mudar os anteriores.
/// Mesma concorrencia da edicao (versao lida, 409). Publica o <see cref="RecurrenceSaved"/> v2.
/// </summary>
public sealed class SetRecurrenceAmountHandler(
    IRecurrenceRepository recurrences,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    PendingAdjustmentRates adjustmentRates,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(SetRecurrenceAmountCommand command, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.NotFound);
        }

        if (command.Version != recurrence.Version)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        var closed = (await recurrences.ListClosedPeriodsAsync(command.OwnerUserId, [recurrence.Id], cancellationToken))
            .GetValueOrDefault(recurrence.Id) ?? new HashSet<string>();
        var set = recurrence.SetAmountFrom(command.FromPeriod?.Trim() ?? string.Empty, command.Amount, closed, timeProvider.GetUtcNow());
        if (set.IsFailure)
        {
            return Result.Failure<RecurrenceDto>(set.Error);
        }

        return await RecurrenceAmountSave.SaveAsync(recurrence, closed, accountsClient, outbox, unitOfWork, adjustmentRates, calendar, timeProvider, cancellationToken);
    }
}

/// <summary>RJT-2: tira uma vigencia que ainda nao comecou (a primeira nunca sai).</summary>
public sealed class RemoveRecurrenceAmountHandler(
    IRecurrenceRepository recurrences,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    PendingAdjustmentRates adjustmentRates,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(
        Guid id, Guid ownerUserId, string fromPeriod, int version, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(id, ownerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.NotFound);
        }

        if (version != recurrence.Version)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        var removed = recurrence.RemoveAmountFrom(fromPeriod, recurrence.PeriodOf(calendar.Today(timeProvider)), timeProvider.GetUtcNow());
        if (removed.IsFailure)
        {
            return Result.Failure<RecurrenceDto>(removed.Error);
        }

        var closed = (await recurrences.ListClosedPeriodsAsync(ownerUserId, [recurrence.Id], cancellationToken))
            .GetValueOrDefault(recurrence.Id) ?? new HashSet<string>();
        return await RecurrenceAmountSave.SaveAsync(recurrence, closed, accountsClient, outbox, unitOfWork, adjustmentRates, calendar, timeProvider, cancellationToken);
    }
}

public sealed record ApplyRecurrenceAdjustmentCommand(Guid Id, Guid OwnerUserId, int Year, decimal? Rate, decimal? Amount, int Version);

/// <summary>
/// RJT-4 (ADR-0010, decisao 4): confirma o reajuste de um aniversario pelo percentual ou pelo valor novo.
/// Cria a vigencia no periodo do aniversario; o mesmo aniversario duas vezes = 409.
/// </summary>
public sealed class ApplyRecurrenceAdjustmentHandler(
    IRecurrenceRepository recurrences,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    PendingAdjustmentRates adjustmentRates,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(ApplyRecurrenceAdjustmentCommand command, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.NotFound);
        }

        if (command.Version != recurrence.Version)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        var closed = (await recurrences.ListClosedPeriodsAsync(command.OwnerUserId, [recurrence.Id], cancellationToken))
            .GetValueOrDefault(recurrence.Id) ?? new HashSet<string>();
        var applied = recurrence.ApplyAdjustment(command.Year, command.Rate, command.Amount, closed, timeProvider.GetUtcNow());
        if (applied.IsFailure)
        {
            return Result.Failure<RecurrenceDto>(applied.Error);
        }

        return await RecurrenceAmountSave.SaveAsync(recurrence, closed, accountsClient, outbox, unitOfWork, adjustmentRates, calendar, timeProvider, cancellationToken);
    }
}

internal static class RecurrenceAmountSave
{
    /// <summary>Publica o estado novo da regra e grava (conflito de versao entre leitura e gravacao = 409).</summary>
    public static async Task<Result<RecurrenceDto>> SaveAsync(
        Recurrence recurrence, IReadOnlySet<string> closed, IAccountsClient accountsClient, IEventOutbox outbox,
        IUnitOfWork unitOfWork, PendingAdjustmentRates adjustmentRates, LocalCalendar calendar, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var today = calendar.Today(timeProvider);
        var accountScope = await accountsClient.FindAccountScopeAsync(recurrence.AccountId, cancellationToken);
        outbox.Add(RecurrenceSaved.From(recurrence, accountScope, recurrence.PeriodOf(today)));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        return RecurrenceDto.From(recurrence, today, closed, await adjustmentRates.ForAsync([recurrence], today, cancellationToken));
    }
}
