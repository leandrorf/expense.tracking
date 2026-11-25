using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Application.Abstractions;

/// <summary>REC (ADR-0008): regras de recorrencia e os periodos ja fechados delas.</summary>
public interface IRecurrenceRepository
{
    Task<Recurrence?> GetAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Recurrence>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    Task AddAsync(Recurrence recurrence, CancellationToken cancellationToken);

    /// <summary>Remove a regra e os periodos fechados dela (os lancamentos feitos a partir dela ficam).</summary>
    Task RemoveAsync(Recurrence recurrence, CancellationToken cancellationToken);

    /// <summary>Periodos fechados (lancados ou pulados) de cada regra do dono; regra sem nenhum fica de fora.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> ListClosedPeriodsAsync(
        Guid ownerUserId, IReadOnlyCollection<Guid> recurrenceIds, CancellationToken cancellationToken);

    /// <summary>REC-3: o periodo fechado de uma regra, se houver.</summary>
    Task<RecurrenceOccurrence?> FindOccurrenceAsync(Guid recurrenceId, string period, CancellationToken cancellationToken);

    /// <summary>REC-3: o periodo fechado pelo lancamento, se ele veio de uma recorrencia.</summary>
    Task<RecurrenceOccurrence?> FindOccurrenceByTransactionAsync(Guid transactionId, CancellationToken cancellationToken);

    /// <summary>RJT-8: regras (de todos os donos) que reajustam por um indice, para republicar a taxa estimada.</summary>
    Task<IReadOnlyList<Recurrence>> ListByAdjustmentIndexAsync(EconomicIndex index, CancellationToken cancellationToken);

    void AddOccurrence(RecurrenceOccurrence occurrence);

    void RemoveOccurrence(RecurrenceOccurrence occurrence);
}

/// <summary>
/// REC (ADR-0008, decisao 8): "hoje" no fuso configurado (<c>TimeZone</c>, o mesmo do reporting), que
/// decide o que ainda vai acontecer e o que ja passou e esta "a lancar".
/// </summary>
public sealed record LocalCalendar(TimeZoneInfo TimeZone)
{
    public DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone).DateTime);

    /// <summary>
    /// O fuso pelo id IANA (padrao <c>America/Sao_Paulo</c>); sem a base de fusos da maquina, tenta o id
    /// do Windows e, por fim, UTC. Mesma resolucao do reporting.
    /// </summary>
    public static LocalCalendar FromId(string? id)
    {
        id ??= "America/Sao_Paulo";
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return new LocalCalendar(zone);
        }

        return new LocalCalendar(
            TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)
                ? zone
                : TimeZoneInfo.Utc);
    }
}
