using Expense.Tracking.Reporting.Application.Projection;

namespace Expense.Tracking.Reporting.Api.Setup;

/// <summary>
/// EVT-2 (ADR-0001): le o feed de eventos do transactions em segundo plano e mantem a projecao.
/// Enquanto houver lotes cheios, le sem esperar; quando o feed esvazia, espera o intervalo de
/// polling. Em erro (transactions ou identity fora do ar, versao de evento nao suportada), registra
/// e tenta de novo depois de <see cref="ErrorBackoff"/>: o checkpoint so avanca em lote gravado.
/// </summary>
public sealed class ProjectionWorker(
    IServiceScopeFactory scopeFactory,
    ProjectionOptions options,
    ProjectionStatus status,
    ILogger<ProjectionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Projecao: worker iniciado (lote {BatchSize}, polling a cada {PollSeconds}s).",
            options.BatchSize, options.PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ProjectionProcessor>();
                var result = await processor.ProcessNextBatchAsync(stoppingToken);
                status.Record(result); // EVT-3: alimenta a metrica de atraso

                if (result.Read > 0)
                {
                    logger.LogInformation(
                        "Projecao: {Applied} eventos aplicados, {Skipped} pulados; checkpoint {Checkpoint}.",
                        result.Applied, result.Skipped, result.Checkpoint);
                }

                delay = result.Read >= options.BatchSize ? TimeSpan.Zero : options.PollInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Projecao: falha ao aplicar lote; nova tentativa em {Seconds}s.", ErrorBackoff.TotalSeconds);
                delay = ErrorBackoff;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
