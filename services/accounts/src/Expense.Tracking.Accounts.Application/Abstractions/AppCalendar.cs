namespace Expense.Tracking.Accounts.Application.Abstractions;

/// <summary>
/// CYC-3 (ADR-0012, decisao 3): "hoje" no fuso do app (<c>TimeZone</c>, o mesmo do transactions e do reporting),
/// que decide qual fatura esta aberta e se uma troca de ciclo mexeria numa fatura ja fechada.
/// </summary>
public sealed record AppCalendar(TimeZoneInfo TimeZone)
{
    public DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone).DateTime);

    /// <summary>
    /// O fuso pelo id IANA (padrao <c>America/Sao_Paulo</c>); sem a base de fusos da maquina, tenta o id do
    /// Windows e, por fim, UTC. Mesma resolucao dos outros servicos.
    /// </summary>
    public static AppCalendar FromId(string? id)
    {
        id ??= "America/Sao_Paulo";
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return new AppCalendar(zone);
        }

        return new AppCalendar(
            TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)
                ? zone
                : TimeZoneInfo.Utc);
    }
}
