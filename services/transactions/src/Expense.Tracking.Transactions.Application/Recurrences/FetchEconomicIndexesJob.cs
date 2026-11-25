using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Application.Recurrences;

/// <summary>RJT-6: configuracao da busca dos indices (<c>Indexes:Fetch:*</c>).</summary>
/// <param name="Since">Primeiro mes a buscar quando o indice ainda nao tem nenhum guardado.</param>
public sealed record IndexFetchOptions(DateOnly Since);

/// <summary>
/// O que uma rodada trouxe: meses novos por indice, regras republicadas por indice (RJT-8) e os indices cuja
/// busca falhou (com o motivo).
/// </summary>
public sealed record IndexFetchResult(
    IReadOnlyDictionary<EconomicIndex, int> Added,
    IReadOnlyDictionary<EconomicIndex, string> Failed,
    IReadOnlyDictionary<EconomicIndex, int>? Republished = null);

/// <summary>
/// RJT-6 (ADR-0010, decisao 6): pede a fonte, para cada indice, os meses que faltam desde o ultimo guardado
/// (ou desde <see cref="IndexFetchOptions.Since"/>) ate o mes corrente e guarda os novos. A falha de um indice
/// fica no resultado e nao impede os outros; a proxima rodada tenta de novo. RJT-8 (decisao 7): com mes novo,
/// as regras que reajustam pelo indice saem de novo no feed (<see cref="RecurrenceSaved"/> com a taxa estimada
/// nova), no mesmo commit dos meses: ou vai tudo, ou nada e a rodada seguinte tenta de novo.
/// </summary>
public sealed class FetchEconomicIndexesJob(
    IEconomicIndexSource source,
    IEconomicIndexRepository indexes,
    IRecurrenceRepository recurrences,
    IAccountScopeDirectory scopes,
    IEventOutbox outbox,
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
        var republished = new Dictionary<EconomicIndex, int>();
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

                republished[index] = fresh.Count == 0 ? 0 : await RepublishAsync(index, fresh, today, cancellationToken);
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

        return new IndexFetchResult(added, failed, republished);
    }

    /// <summary>
    /// Publica de novo as regras do indice com o acumulado que inclui os meses novos (ainda nao gravados).
    /// Regra cuja conta o accounts nao devolve fica de fora (o escopo nao pode ir nulo).
    /// </summary>
    private async Task<int> RepublishAsync(
        EconomicIndex index, IReadOnlyList<IndexMonthRate> fresh, DateOnly today, CancellationToken cancellationToken)
    {
        var rules = await recurrences.ListByAdjustmentIndexAsync(index, cancellationToken);
        if (rules.Count == 0)
        {
            return 0;
        }

        var stored = await indexes.ListAsync(index, EconomicIndexValue.MonthOf(today.AddMonths(-24)), EconomicIndexValue.MonthOf(today), cancellationToken);
        var estimated = RecurrenceAdjustment.LatestAccumulated(
            stored.Select(s => (s.Month, s.Rate)).Concat(fresh.Select(f => (f.Month, f.Rate))));
        var scopeOf = await scopes.GetScopesAsync(rules.Select(r => r.AccountId).Distinct().ToList(), cancellationToken);
        var count = 0;
        foreach (var rule in rules)
        {
            if (scopeOf.TryGetValue(rule.AccountId, out var scope))
            {
                outbox.Add(RecurrenceSaved.From(rule, scope, rule.PeriodOf(today), estimated));
                count++;
            }
        }

        return count;
    }
}
