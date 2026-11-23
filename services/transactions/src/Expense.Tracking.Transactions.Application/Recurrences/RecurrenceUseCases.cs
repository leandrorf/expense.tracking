using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Recurrences;

/// <summary>Proxima ocorrencia em aberto de hoje em diante: o periodo que a identifica e a data.</summary>
public sealed record NextOccurrenceDto(string Period, DateOnly ScheduledOn);

public sealed record RecurrenceDto(
    Guid Id,
    Guid AccountId,
    string Type,
    string PaymentMethod,
    Guid? CreditCardId,
    string Description,
    string Category,
    decimal Amount,
    string Currency,
    string? Notes,
    string Frequency,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int Version,
    NextOccurrenceDto? NextOccurrence,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>Quantas datas a frente procurar a proxima em aberto (cobre 50 anos da anual).</summary>
    private const int MaxLookAheadYears = 50;

    public static RecurrenceDto From(Recurrence r, DateOnly today, IReadOnlySet<string> closedPeriods)
    {
        var next = r.Occurrences(today, today.AddYears(MaxLookAheadYears))
            .Where(o => !closedPeriods.Contains(o.Period))
            .Select(o => new NextOccurrenceDto(o.Period, o.ScheduledOn))
            .FirstOrDefault();
        return new(r.Id, r.AccountId, r.Type.ToString(), r.PaymentMethod.ToString(), r.CreditCardId, r.Description,
            r.Category, r.Amount, r.Currency, r.Notes, r.Frequency.ToString(), r.StartsOn, r.EndsOn, r.Version, next,
            r.CreatedAtUtc);
    }
}

/// <summary>Os campos da regra como o cliente os manda (criar e editar).</summary>
public sealed record RecurrenceFields(
    TransactionType Type,
    PaymentMethod PaymentMethod,
    Guid? CreditCardId,
    string? Description,
    string? Category,
    decimal Amount,
    string? Currency,
    string? Notes,
    RecurrenceFrequency Frequency,
    DateOnly StartsOn,
    DateOnly? EndsOn);

public sealed record CreateRecurrenceCommand(Guid OwnerUserId, Guid AccountId, RecurrenceFields Fields);

public sealed record UpdateRecurrenceCommand(Guid Id, Guid OwnerUserId, Guid AccountId, RecurrenceFields Fields, int Version);

internal static class RecurrenceErrors
{
    public static readonly Error NotFound = Error.NotFound("recurrence.not_found", "Recorrencia nao encontrada.");

    public static readonly Error ConcurrencyConflict = Error.Conflict(
        "recurrence.concurrency_conflict", "A recorrencia mudou depois que voce a leu. Recarregue e tente de novo.");
}

/// <summary>
/// REC-2 (ADR-0008, decisoes 2 e 5): cria a regra com as validacoes do lancamento, confere conta e cartao
/// no accounts (a mesma consulta traz o escopo PF/PJ, que vai no evento) e publica o
/// <see cref="RecurrenceSaved"/> no mesmo commit.
/// </summary>
public sealed class CreateRecurrenceHandler(
    IRecurrenceRepository recurrences,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(CreateRecurrenceCommand command, CancellationToken cancellationToken)
    {
        var f = command.Fields;
        var created = Recurrence.Create(command.OwnerUserId, command.AccountId, f.Type, f.PaymentMethod, f.CreditCardId,
            f.Description, f.Category, f.Amount, f.Currency, f.Notes, f.Frequency, f.StartsOn, f.EndsOn, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<RecurrenceDto>(created.Error);
        }

        var accountScope = await accountsClient.FindAccountScopeAsync(command.AccountId, cancellationToken);
        if (accountScope is null)
        {
            return Result.Failure<RecurrenceDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
        }

        if (f.CreditCardId is { } cardId && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<RecurrenceDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        await recurrences.AddAsync(created.Value, cancellationToken);
        outbox.Add(RecurrenceSaved.From(created.Value, accountScope));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RecurrenceDto.From(created.Value, calendar.Today(timeProvider), new HashSet<string>());
    }
}

public sealed class ListRecurrencesHandler(IRecurrenceRepository recurrences, LocalCalendar calendar, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RecurrenceDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var items = await recurrences.ListAsync(ownerUserId, cancellationToken);
        var closed = await recurrences.ListClosedPeriodsAsync(ownerUserId, items.Select(r => r.Id).ToArray(), cancellationToken);
        var today = calendar.Today(timeProvider);
        return items
            .Select(r => RecurrenceDto.From(r, today, closed.GetValueOrDefault(r.Id) ?? new HashSet<string>()))
            .ToList();
    }
}

public sealed class GetRecurrenceHandler(IRecurrenceRepository recurrences, LocalCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(id, ownerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.NotFound);
        }

        var closed = await recurrences.ListClosedPeriodsAsync(ownerUserId, [id], cancellationToken);
        return RecurrenceDto.From(recurrence, calendar.Today(timeProvider), closed.GetValueOrDefault(id) ?? new HashSet<string>());
    }
}

/// <summary>
/// REC-2 (ADR-0008, decisao 6): corrige a regra inteira com a versao lida (409 se mudou). A conta nao
/// muda. Edicao sem mudanca = 200 sem versao nova e sem evento. So os periodos em aberto mudam.
/// </summary>
public sealed class UpdateRecurrenceHandler(
    IRecurrenceRepository recurrences,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(UpdateRecurrenceCommand command, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.NotFound);
        }

        if (command.AccountId != recurrence.AccountId)
        {
            return Result.Failure<RecurrenceDto>(Error.Validation(
                "recurrence.account_immutable", "A conta de uma recorrencia nao muda: exclua e crie de novo na outra conta."));
        }

        if (command.Version != recurrence.Version)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        var f = command.Fields;
        var previousCard = recurrence.CreditCardId;
        var edited = recurrence.Edit(f.Type, f.PaymentMethod, f.CreditCardId, f.Description, f.Category, f.Amount,
            f.Currency, f.Notes, f.Frequency, f.StartsOn, f.EndsOn, timeProvider.GetUtcNow());
        if (edited.IsFailure)
        {
            return Result.Failure<RecurrenceDto>(edited.Error);
        }

        var closed = await recurrences.ListClosedPeriodsAsync(command.OwnerUserId, [recurrence.Id], cancellationToken);
        var closedPeriods = closed.GetValueOrDefault(recurrence.Id) ?? new HashSet<string>();
        if (!edited.Value)
        {
            return RecurrenceDto.From(recurrence, calendar.Today(timeProvider), closedPeriods);
        }

        if (recurrence.CreditCardId is { } cardId && cardId != previousCard
            && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<RecurrenceDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        // O escopo e o da conta, que nao muda; a consulta so o traz de novo para o evento.
        var accountScope = await accountsClient.FindAccountScopeAsync(recurrence.AccountId, cancellationToken);
        outbox.Add(RecurrenceSaved.From(recurrence, accountScope));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        return RecurrenceDto.From(recurrence, calendar.Today(timeProvider), closedPeriods);
    }
}

/// <summary>
/// REC-2 (ADR-0008, decisao 6): exclui a regra. As ocorrencias em aberto saem da previsao
/// (<see cref="RecurrenceDeleted"/>); os lancamentos feitos a partir dela ficam como estao.
/// </summary>
public sealed class DeleteRecurrenceHandler(IRecurrenceRepository recurrences, IEventOutbox outbox, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(id, ownerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure(RecurrenceErrors.NotFound);
        }

        await recurrences.RemoveAsync(recurrence, cancellationToken);
        outbox.Add(new RecurrenceDeleted(recurrence.Id, recurrence.OwnerUserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
