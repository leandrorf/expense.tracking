using Expense.Tracking.Transactions.Application.Recurrences;

namespace Expense.Tracking.Transactions.Api.Setup;

/// <summary>
/// RJT-6 (ADR-0010, decisao 6): busca uma vez por dia as variacoes mensais do IPCA, INPC e IGP-M que faltam.
/// So roda com <c>Indexes:Fetch:Enabled</c> (padrao desligado: nada sai para a internet). Fonte fora do ar
/// ou resposta invalida: registra no log e tenta na rodada seguinte; nada mais e bloqueado.
/// </summary>
public sealed class IndexFetchWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<IndexFetchWorker> logger) : BackgroundService
{
    public const string EnabledKey = "Indexes:Fetch:Enabled";
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = Interval;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<FetchEconomicIndexesJob>().RunAsync(stoppingToken);
                foreach (var (index, count) in result.Added.Where(a => a.Value > 0))
                {
                    logger.LogInformation("Indices: {Count} meses novos do {Index} guardados; {Rules} regras republicadas com a taxa estimada nova.",
                        count, index, result.Republished?.GetValueOrDefault(index) ?? 0);
                }

                foreach (var (index, reason) in result.Failed)
                {
                    logger.LogWarning("Indices: a busca do {Index} falhou ({Reason}); nova tentativa em {Hours} h.",
                        index, reason, Interval.TotalHours);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Indices: a rodada falhou; nova tentativa em {Hours} h.", Interval.TotalHours);
            }
        }
    }
}
