using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Recurrences;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>RJT-6 (ADR-0010, decisao 6): o worker dos indices vem desligado e o job guarda os meses novos.</summary>
public sealed class EconomicIndexesTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    [Fact]
    public void Worker_IsOffByDefault_SoNothingGoesToTheInternet()
    {
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s is IndexFetchWorker);
    }

    [Fact]
    public async Task Job_StoresTheNewMonths_InTheDatabase()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var source = new FixedSource([new("2026-08", 0.23m), new("2026-09", 0.48m)]);
        var job = new FetchEconomicIndexesJob(source, provider.GetRequiredService<IEconomicIndexRepository>(),
            provider.GetRequiredService<IUnitOfWork>(), new IndexFetchOptions(new DateOnly(2026, 8, 1)),
            provider.GetRequiredService<LocalCalendar>(), TimeProvider.System);

        var result = await job.RunAsync(CancellationToken.None);

        Assert.Empty(result.Failed);
        using var check = factory.Services.CreateScope();
        var stored = await check.ServiceProvider.GetRequiredService<IEconomicIndexRepository>()
            .ListAsync(EconomicIndex.IGPM, "2026-01", "2026-12", CancellationToken.None);
        Assert.Equal([("2026-08", 0.23m), ("2026-09", 0.48m)], stored.Select(v => (v.Month, v.Rate)));
    }

    private sealed class FixedSource(IReadOnlyList<IndexMonthRate> rates) : IEconomicIndexSource
    {
        public Task<IReadOnlyList<IndexMonthRate>> FetchAsync(EconomicIndex index, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult(rates);
    }
}
