using Expense.Tracking.Reporting.Application.Reminders;

namespace Expense.Tracking.Reporting.Api.Setup;

/// <summary>NTF-8: de quanto em quanto tempo o job do resumo roda (padrao: de hora em hora).</summary>
public sealed record ReminderDigestOptions(bool Enabled, TimeSpan Interval, TimeSpan FirstDelay)
{
    public static ReminderDigestOptions From(IConfiguration configuration) => new(
        configuration.GetValue("Reminders:Digest:Enabled", true),
        TimeSpan.FromSeconds(configuration.GetValue("Reminders:Digest:IntervalSeconds", 3600)),
        TimeSpan.FromSeconds(configuration.GetValue("Reminders:Digest:FirstDelaySeconds", 30)));
}

/// <summary>
/// NTF-8 (ADR-0009, decisao 6): roda o <see cref="SendReminderDigestsJob"/> a cada intervalo. O job decide
/// quem recebe (hora escolhida, uma vez por dia, so com aviso novo); falha numa passada nao para o worker.
/// </summary>
public sealed class ReminderDigestWorker(
    IServiceScopeFactory scopeFactory,
    ReminderDigestOptions options,
    TimeProvider timeProvider,
    ILogger<ReminderDigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Resumo de avisos por e-mail desligado (Reminders:Digest:Enabled=false).");
            return;
        }

        var delay = options.FirstDelay;
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

            delay = options.Interval;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<SendReminderDigestsJob>().RunOnceAsync(stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Resumos de avisos enviados: {Sent}.", sent);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Passada do resumo de avisos falhou; tenta de novo no proximo intervalo.");
            }
        }
    }
}
