using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Application.Recurrences;

/// <summary>RJT-6: configuracao da busca dos indices (<c>Indexes:Fetch:*</c>).</summary>
/// <param name="Since">Primeiro mes a buscar quando o indice ainda nao tem nenhum guardado.</param>
public sealed record IndexFetchOptions(DateOnly Since);

/// <summary>O que uma rodada trouxe: meses novos por indice e os indices cuja busca falhou (com o motivo).</summary>
public sealed record IndexFetchResult(IReadOnlyDictionary<EconomicIndex, int> Added, IReadOnlyDictionary<EconomicIndex, string> Failed);

/// <summary>
/// RJT-6 (ADR-0010, decisao 6): pede a fonte, para cada indice, os meses que faltam desde o ultimo guardado
/// (ou desde <see cref="IndexFetchOptions.Since"/>) ate o mes corrente e guarda os novos. A falha de um indice
/// fica no resultado e nao impede os outros; a proxima rodada tenta de novo.
/// </summary>
public sealed class FetchEconomicIndexesJob(
    IEconomicIndexSource source,
    IEconomicIndexRepository indexes,
    IUnitOfWork unitOfWork,
    IndexFetchOptions options,
    LocalCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<IndexFetchResult> RunAsync(CancellationToken cancellationToken)
    {
        var today = calendar.Today(timeProvider);
        var added = new Dictionary<EconomicIndex, int>();
        var failed = new Dictionary<EconomicIndex, string>();
        foreach (var index in Enum.GetValues<EconomicIndex>())
        {
            try
            {
                var last = await indexes.LastMonthAsync(index, cancellationToken);
                var from = last is null
                    ? new DateOnly(options.Since.Year, options.Since.Month, 1)
                    : DateOnly.ParseExact(last + "-01", "yyyy-MM-dd").AddMonths(1);
                if (from > today)
                {
                    added[index] = 0;
                    continue;
                }

                var fetched = await source.FetchAsync(index, from, today, cancellationToken);
                var now = timeProvider.GetUtcNow();
                var fresh = fetched
                    .Where(r => string.CompareOrdinal(r.Month, EconomicIndexValue.MonthOf(from)) >= 0)
                    .DistinctBy(r => r.Month)
                    .ToList();
                foreach (var rate in fresh)
                {
                    indexes.Add(EconomicIndexValue.Create(index, rate.Month, rate.Rate, now));
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                added[index] = fresh.Count;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // O que ficou pela metade nao vai junto com o proximo indice.
                unitOfWork.Reset();
                failed[index] = ex.Message;
            }
        }

        return new IndexFetchResult(added, failed);
    }
}
