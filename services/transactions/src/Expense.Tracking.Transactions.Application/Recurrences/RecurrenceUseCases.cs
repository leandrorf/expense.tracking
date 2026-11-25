using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Recurrences;

/// <summary>Proxima ocorrencia em aberto de hoje em diante: o periodo, a data e o valor estimado dela (RJT-2).</summary>
public sealed record NextOccurrenceDto(string Period, DateOnly ScheduledOn, decimal Amount);

/// <summary>RJT-2: uma vigencia do valor.</summary>
public sealed record RecurrenceAmountDto(string FromPeriod, decimal Amount);

/// <summary>RJT-4 (ADR-0010, decisao 3): o reajuste anual configurado.</summary>
public sealed record RecurrenceAdjustmentDto(int AnniversaryMonth, string Kind, string? Index, decimal? Rate, bool NoReduction)
{
    public static RecurrenceAdjustmentDto? From(RecurrenceAdjustment? a) => a is null
        ? null
        : new(a.AnniversaryMonth, a.Kind.ToString(), a.Index?.ToString(), a.Rate, a.NoReduction);
}

/// <summary>
/// RJT-4 (ADR-0010, decisao 5): reajuste do aniversario ainda nao aplicado, com a sugestao quando ha. RJT-7:
/// <c>waitingFor</c> = o mes de indice que falta para sugerir (o reajuste pode ser confirmado a mao assim mesmo).
/// </summary>
public sealed record PendingAdjustmentDto(
    int Year, string Period, decimal CurrentAmount, decimal? Rate, decimal? SuggestedAmount, string? WaitingFor);

/// <summary>
/// RJT-7: variacoes mensais guardadas dos indices que as regras usam, nos meses que um reajuste pendente
/// pode pedir (do aniversario mais antigo possivel, 23 meses atras, ate hoje).
/// </summary>
public sealed class PendingAdjustmentRates(IEconomicIndexRepository indexes)
{
    public async Task<IndexRateLookup> ForAsync(IEnumerable<Recurrence> rules, DateOnly today, CancellationToken cancellationToken)
    {
        var used = rules.Select(r => r.Adjustment).Where(a => a?.Index is not null).Select(a => a!.Index!.Value).Distinct().ToList();
        if (used.Count == 0)
        {
            return (_, _) => null;
        }

        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-24);
        var rates = new Dictionary<(EconomicIndex, string), decimal>();
        foreach (var index in used)
        {
            foreach (var value in await indexes.ListAsync(index, EconomicIndexValue.MonthOf(first), EconomicIndexValue.MonthOf(today), cancellationToken))
            {
                rates[(index, value.Month)] = value.Rate;
            }
        }

        return (index, month) => rates.TryGetValue((index, month), out var rate) ? rate : null;
    }
}

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
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<RecurrenceAmountDto> Amounts,
    RecurrenceAdjustmentDto? Adjustment,
    PendingAdjustmentDto? PendingAdjustment)
{
    /// <summary>Quantas datas a frente procurar a proxima em aberto (cobre 50 anos da anual).</summary>
    private const int MaxLookAheadYears = 50;

    public static RecurrenceDto From(Recurrence r, DateOnly today, IReadOnlySet<string> closedPeriods, IndexRateLookup? indexRates = null)
    {
        var next = r.Occurrences(today, today.AddYears(MaxLookAheadYears))
            .Where(o => !closedPeriods.Contains(o.Period))
            .Select(o => new NextOccurrenceDto(o.Period, o.ScheduledOn, r.AmountFor(o.Period)))
            .FirstOrDefault();
        // RJT-2: "amount" e o valor vigente hoje (o mesmo que o PUT corrige); as vigencias vem em "amounts".
        return new(r.Id, r.AccountId, r.Type.ToString(), r.PaymentMethod.ToString(), r.CreditCardId, r.Description,
            r.Category, r.AmountFor(r.PeriodOf(today)), r.Currency, r.Notes, r.Frequency.ToString(), r.StartsOn, r.EndsOn,
            r.Version, next, r.CreatedAtUtc, r.Amounts.Select(a => new RecurrenceAmountDto(a.FromPeriod, a.Amount)).ToList(),
            RecurrenceAdjustmentDto.From(r.Adjustment),
            r.PendingAdjustment(today, indexRates) is { } p
                ? new PendingAdjustmentDto(p.Year, p.Period, p.CurrentAmount, p.Rate, p.SuggestedAmount, p.WaitingFor)
                : null);
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
    DateOnly? EndsOn,
    RecurrenceAdjustmentSpec? Adjustment = null);

/// <param name="FromTransactionId">REC-10: lancamento que vira a ocorrencia do proprio periodo ("repetir todo mes").</param>
public sealed record CreateRecurrenceCommand(Guid OwnerUserId, Guid AccountId, RecurrenceFields Fields, Guid? FromTransactionId = null);

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
    ITransactionRepository transactions,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    PendingAdjustmentRates adjustmentRates,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(CreateRecurrenceCommand command, CancellationToken cancellationToken)
    {
        var f = command.Fields;
        var created = Recurrence.Create(command.OwnerUserId, command.AccountId, f.Type, f.PaymentMethod, f.CreditCardId,
            f.Description, f.Category, f.Amount, f.Currency, f.Notes, f.Frequency, f.StartsOn, f.EndsOn, timeProvider.GetUtcNow(),
            f.Adjustment);
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

        // REC-10: o lancamento de origem fecha o periodo dele na regra nova (mesma conta, sem vinculo anterior).
        var closedPeriods = new HashSet<string>();
        if (command.FromTransactionId is { } fromId)
        {
            var origin = await transactions.GetAsync(fromId, command.OwnerUserId, cancellationToken);
            if (origin is null)
            {
                return Result.Failure<RecurrenceDto>(Error.NotFound("transaction.not_found", "Lancamento nao encontrado."));
            }

            var period = created.Value.PeriodOf(origin.OccurredOn);
            if (origin.AccountId != command.AccountId || origin.RecurrenceId is not null || created.Value.ScheduledOn(period) is null)
            {
                return Result.Failure<RecurrenceDto>(Error.Validation(
                    "recurrence.from_transaction_invalid",
                    "O lancamento de origem precisa ser da mesma conta, cair na agenda da recorrencia e nao vir de outra recorrencia."));
            }

            origin.LinkToRecurrence(created.Value.Id, period);
            recurrences.AddOccurrence(RecurrenceOccurrence.Launched(created.Value, period, origin.Id, timeProvider.GetUtcNow()));
            closedPeriods.Add(period);
        }

        await recurrences.AddAsync(created.Value, cancellationToken);
        outbox.Add(RecurrenceSaved.From(created.Value, accountScope, created.Value.PeriodOf(calendar.Today(timeProvider))));
        if (command.FromTransactionId is { } linked)
        {
            outbox.Add(new RecurrenceOccurrenceClosed(created.Value.Id, created.Value.OwnerUserId,
                closedPeriods.Single(), nameof(OccurrenceStatus.Launched), linked));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var today = calendar.Today(timeProvider);
        return RecurrenceDto.From(created.Value, today, closedPeriods,
            await adjustmentRates.ForAsync([created.Value], today, cancellationToken));
    }
}

public sealed class ListRecurrencesHandler(
    IRecurrenceRepository recurrences, PendingAdjustmentRates adjustmentRates, LocalCalendar calendar, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RecurrenceDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var items = await recurrences.ListAsync(ownerUserId, cancellationToken);
        var closed = await recurrences.ListClosedPeriodsAsync(ownerUserId, items.Select(r => r.Id).ToArray(), cancellationToken);
        var today = calendar.Today(timeProvider);
        var rates = await adjustmentRates.ForAsync(items, today, cancellationToken);
        return items
            .Select(r => RecurrenceDto.From(r, today, closed.GetValueOrDefault(r.Id) ?? new HashSet<string>(), rates))
            .ToList();
    }
}

public sealed class GetRecurrenceHandler(
    IRecurrenceRepository recurrences, PendingAdjustmentRates adjustmentRates, LocalCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<RecurrenceDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var recurrence = await recurrences.GetAsync(id, ownerUserId, cancellationToken);
        if (recurrence is null)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.NotFound);
        }

        var closed = await recurrences.ListClosedPeriodsAsync(ownerUserId, [id], cancellationToken);
        var today = calendar.Today(timeProvider);
        return RecurrenceDto.From(recurrence, today, closed.GetValueOrDefault(id) ?? new HashSet<string>(),
            await adjustmentRates.ForAsync([recurrence], today, cancellationToken));
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
    PendingAdjustmentRates adjustmentRates,
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
        // RJT-2 (ADR-0010, decisao 2): o valor do PUT corrige a vigencia de hoje.
        var currentPeriod = recurrence.PeriodOf(calendar.Today(timeProvider));
        var edited = recurrence.Edit(f.Type, f.PaymentMethod, f.CreditCardId, f.Description, f.Category, f.Amount,
            f.Currency, f.Notes, f.Frequency, f.StartsOn, f.EndsOn, timeProvider.GetUtcNow(), currentPeriod, f.Adjustment);
        if (edited.IsFailure)
        {
            return Result.Failure<RecurrenceDto>(edited.Error);
        }

        var closed = await recurrences.ListClosedPeriodsAsync(command.OwnerUserId, [recurrence.Id], cancellationToken);
        var closedPeriods = closed.GetValueOrDefault(recurrence.Id) ?? new HashSet<string>();
        var today = calendar.Today(timeProvider);
        var rates = await adjustmentRates.ForAsync([recurrence], today, cancellationToken);
        if (!edited.Value)
        {
            return RecurrenceDto.From(recurrence, today, closedPeriods, rates);
        }

        if (recurrence.CreditCardId is { } cardId && cardId != previousCard
            && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<RecurrenceDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        // O escopo e o da conta, que nao muda; a consulta so o traz de novo para o evento.
        var accountScope = await accountsClient.FindAccountScopeAsync(recurrence.AccountId, cancellationToken);
        outbox.Add(RecurrenceSaved.From(recurrence, accountScope, recurrence.PeriodOf(calendar.Today(timeProvider))));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Result.Failure<RecurrenceDto>(RecurrenceErrors.ConcurrencyConflict);
        }

        return RecurrenceDto.From(recurrence, today, closedPeriods, rates);
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
