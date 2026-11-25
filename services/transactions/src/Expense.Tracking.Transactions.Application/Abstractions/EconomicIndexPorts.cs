using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Application.Abstractions;

/// <summary>Uma variacao mensal lida da fonte: mes (<c>yyyy-MM</c>) e variacao em %.</summary>
public sealed record IndexMonthRate(string Month, decimal Rate);

/// <summary>
/// RJT-6 (ADR-0010, decisao 6): fonte dos indices oficiais (API SGS do Banco Central). Nenhum dado de usuario
/// vai na chamada: so o indice e o intervalo de meses.
/// </summary>
public interface IEconomicIndexSource
{
    /// <summary>Variacoes mensais de <paramref name="from"/> a <paramref name="to"/> (meses inclusive); vazio quando nao ha.</summary>
    Task<IReadOnlyList<IndexMonthRate>> FetchAsync(EconomicIndex index, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>RJT-6: variacoes mensais guardadas (<c>economic_indexes</c>).</summary>
public interface IEconomicIndexRepository
{
    /// <summary>Ultimo mes guardado do indice (<c>yyyy-MM</c>), ou nulo se nenhum.</summary>
    Task<string?> LastMonthAsync(EconomicIndex index, CancellationToken cancellationToken);

    /// <summary>Variacoes do indice entre dois meses (<c>yyyy-MM</c>, inclusive), em ordem.</summary>
    Task<IReadOnlyList<EconomicIndexValue>> ListAsync(EconomicIndex index, string fromMonth, string toMonth, CancellationToken cancellationToken);

    void Add(EconomicIndexValue value);
}
