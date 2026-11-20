using Expense.Tracking.Reporting.Application.Projection;
using Expense.Tracking.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// EVT-2: o processador aplica o feed no banco do reporting, com projecao e checkpoint juntos.
/// Os testes da classe dividem banco e feed; cada um confere so os proprios lancamentos.
/// </summary>
public sealed class ProjectionTests(ReportingApiFactory factory) : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _factory = factory;

    [Fact]
    public async Task Processor_AppliesRegistrationsAndDeletions_AndStoresTheCheckpoint()
    {
        var owner = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        _factory.EventFeed.Registered(kept, owner, accountScope: "Business", amount: 250m);
        _factory.EventFeed.Registered(deleted, owner, amount: 40m);
        var last = _factory.EventFeed.Deleted(deleted, owner);

        await DrainAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var rows = await db.Transactions.Where(t => t.OwnerUserId == owner).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(kept, row.TransactionId);
        Assert.Equal("Business", row.AccountScope);
        Assert.Equal(250m, row.Amount);
        Assert.Equal(new DateOnly(2026, 11, 19), row.OccurredOn);
        Assert.True((await db.Checkpoints.SingleAsync()).LastSequence >= last.Sequence);
    }

    [Fact]
    public async Task ZeroingTheCheckpoint_RebuildsTheSameProjection_WithoutDuplicatingValues()
    {
        var owner = Guid.NewGuid();
        _factory.EventFeed.Registered(Guid.NewGuid(), owner, amount: 10m);
        _factory.EventFeed.Registered(Guid.NewGuid(), owner, accountScope: null, amount: 20m);
        var removed = Guid.NewGuid();
        _factory.EventFeed.Registered(removed, owner, amount: 999m);
        _factory.EventFeed.Deleted(removed, owner);
        await DrainAsync();
        var before = await SnapshotAsync(owner);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            (await db.Checkpoints.SingleAsync()).LastSequence = 0;
            await db.SaveChangesAsync();
        }

        await DrainAsync();
        var after = await SnapshotAsync(owner);

        Assert.Equal(2, before.Count);
        Assert.Equal(before, after);
        Assert.Equal(30m, after.Sum(r => r.Amount));
    }

    private async Task DrainAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ProjectionProcessor>()
                .ProcessNextBatchAsync(CancellationToken.None);
            if (result.Read == 0)
            {
                return;
            }
        }

        Assert.Fail("O feed nao esvaziou em 100 lotes.");
    }

    private async Task<List<(Guid Id, string? Scope, decimal Amount)>> SnapshotAsync(Guid owner)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        return (await db.Transactions.AsNoTracking().Where(t => t.OwnerUserId == owner).ToListAsync())
            .Select(t => (t.TransactionId, t.AccountScope, t.Amount))
            .OrderBy(t => t.TransactionId)
            .ToList();
    }
}

/// <summary>EVT-2: com o worker ligado, a projecao acompanha o feed sozinha, em segundo plano.</summary>
public sealed class ProjectionWorkerTests(ProjectionWorkerTests.WorkerFactory factory)
    : IClassFixture<ProjectionWorkerTests.WorkerFactory>
{
    private readonly WorkerFactory _factory = factory;

    [Fact]
    public async Task Worker_KeepsTheProjectionInSyncWithTheFeed()
    {
        _ = _factory.Server; // sobe o host (e o worker)
        var owner = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        _factory.EventFeed.Registered(transactionId, owner, amount: 77m);
        Assert.True(await WaitForAsync(async db => await db.Transactions.AnyAsync(t => t.TransactionId == transactionId)));

        _factory.EventFeed.Deleted(transactionId, owner);
        Assert.True(await WaitForAsync(async db => !await db.Transactions.AnyAsync(t => t.TransactionId == transactionId)));
    }

    private async Task<bool> WaitForAsync(Func<ReportingDbContext, Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            if (await condition(scope.ServiceProvider.GetRequiredService<ReportingDbContext>()))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    public sealed class WorkerFactory : ReportingApiFactory
    {
        protected override void ConfigureTestSettings(IDictionary<string, string?> settings)
        {
            settings["Projection:Enabled"] = "true";
            settings["Projection:PollIntervalSeconds"] = "0.05";
        }
    }
}
