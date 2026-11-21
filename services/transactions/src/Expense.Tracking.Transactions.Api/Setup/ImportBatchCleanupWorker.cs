using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Api.Setup;

/// <summary>
/// IMP-4 (ADR-0004, decisao 2): apaga os lotes de importacao pendentes que venceram (7 dias sem
/// confirmar), para a previa de um extrato nao ficar guardada para sempre. Roda de hora em hora.
/// </summary>
public sealed class ImportBatchCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ImportBatchCleanupWorker> logger) : BackgroundService
{
    public const string EnabledKey = "Imports:Cleanup:Enabled";
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
                var batches = scope.ServiceProvider.GetRequiredService<IImportBatchRepository>();
                int deleted;
                var total = 0;
                do
                {
                    deleted = await batches.DeleteExpiredAsync(timeProvider.GetUtcNow(), stoppingToken);
                    total += deleted;
                }
                while (deleted > 0 && !stoppingToken.IsCancellationRequested);

                if (total > 0)
                {
                    logger.LogInformation("Limpeza de importacoes: {Count} lotes pendentes vencidos apagados.", total);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Limpeza de importacoes falhou; nova tentativa em {Minutes} min.", Interval.TotalMinutes);
            }
        }
    }
}
