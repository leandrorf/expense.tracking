using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;

namespace Expense.Tracking.Transactions.Api.Setup;

/// <summary>
/// EVT-4: roda o <see cref="AccountScopeBackfill"/> uma vez por subida, em segundo plano, depois
/// da carga inicial do outbox. Depende do accounts e do identity, que podem subir depois: em
/// falha, tenta de novo com espera crescente (ate <see cref="MaxDelay"/>) e para ao concluir.
/// </summary>
public sealed class AccountScopeBackfillWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AccountScopeBackfillWorker> logger) : BackgroundService
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var result = await AccountScopeBackfill.RunAsync(
                    scope.ServiceProvider.GetRequiredService<TransactionsDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IAccountScopeDirectory>(),
                    timeProvider,
                    stoppingToken);

                if (result.Resolved > 0 || result.Unresolved > 0)
                {
                    logger.LogInformation(
                        "Backfill de escopos: {Resolved} lancamentos legados resolvidos; {Unresolved} sem conta conhecida no accounts.",
                        result.Resolved, result.Unresolved);
                }

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Backfill de escopos falhou; nova tentativa em {Seconds}s.", delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
        }
    }
}
