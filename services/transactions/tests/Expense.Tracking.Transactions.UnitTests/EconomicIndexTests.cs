using System.Net;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Application.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Infrastructure.Clients;

namespace Expense.Tracking.Transactions.UnitTests;

/// <summary>RJT-6 (ADR-0010, decisao 6): leitura da API SGS do Banco Central e a busca diaria dos indices.</summary>
public sealed class EconomicIndexTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T15:00:00Z");
    private static readonly LocalCalendar Calendar = LocalCalendar.FromId("America/Sao_Paulo");

    // ---------- Cliente do SGS ----------

    [Fact]
    public async Task Source_AsksTheSeriesOfTheIndex_AndReadsMonthsAndRates()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """[{"data":"01/09/2026","valor":"0.82"},{"data":"01/10/2026","valor":"-0.14"}]""");
        var source = new BcbSgsIndexSource(new HttpClient(handler) { BaseAddress = new Uri("https://sgs.test/") });

        var rates = await source.FetchAsync(EconomicIndex.IGPM, new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 24), CancellationToken.None);

        Assert.Equal([new IndexMonthRate("2026-09", 0.82m), new IndexMonthRate("2026-10", -0.14m)], rates);
        Assert.Equal("https://sgs.test/dados/serie/bcdata.sgs.189/dados?formato=json&dataInicial=01/09/2026&dataFinal=24/11/2026",
            handler.LastUri!.ToString());
    }

    [Fact]
    public async Task Source_TreatsNotFoundAsNoData_AndOtherErrorsAsFailures()
    {
        var notFound = new BcbSgsIndexSource(new HttpClient(new StubHandler(HttpStatusCode.NotFound, """{"erro":{}}"""))
        { BaseAddress = new Uri("https://sgs.test/") });
        var down = new BcbSgsIndexSource(new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, ""))
        { BaseAddress = new Uri("https://sgs.test/") });

        Assert.Empty(await notFound.FetchAsync(EconomicIndex.IPCA, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            down.FetchAsync(EconomicIndex.IPCA, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 24), CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"valor":"0.1"}""")]
    [InlineData("""[{"data":"2026-10-01","valor":"0.1"}]""")]
    [InlineData("""[{"data":"01/10/2026","valor":"0,1"}]""")]
    [InlineData("""[{"data":"01/10/2026","valor":0.1}]""")]
    public void Parse_RefusesAnswersOutOfTheFormat(string json)
    {
        Assert.Throws<InvalidDataException>(() => BcbSgsIndexSource.Parse(JsonDocument.Parse(json)));
    }

    // ---------- Busca diaria ----------

    [Fact]
    public async Task Job_FetchesFromSinceWhenEmpty_AndThenOnlyTheMissingMonths()
    {
        var source = new FakeSource();
        source.Rates[EconomicIndex.IPCA] = [new("2026-09", 0.48m), new("2026-10", 0.09m)];
        var repository = new FakeRepository();
        var job = Job(source, repository);

        var first = await job.RunAsync(CancellationToken.None);

        Assert.Equal(2, first.Added[EconomicIndex.IPCA]);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 24)), source.Requests[EconomicIndex.IPCA][0]);
        Assert.Equal(["2026-09", "2026-10"], repository.Values.Where(v => v.Index == EconomicIndex.IPCA).Select(v => v.Month));

        // Na rodada seguinte, pede a partir do mes depois do ultimo guardado; o que ja tem nao entra de novo.
        source.Rates[EconomicIndex.IPCA] = [new("2026-10", 0.09m), new("2026-11", 0.30m)];
        var second = await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, second.Added[EconomicIndex.IPCA]);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 24)), source.Requests[EconomicIndex.IPCA][1]);
        Assert.Equal(3, repository.Values.Count(v => v.Index == EconomicIndex.IPCA));
    }

    [Fact]
    public async Task Job_KeepsGoingWhenAnIndexFails_AndSkipsIndexesAlreadyUpToDate()
    {
        var source = new FakeSource { FailOn = EconomicIndex.INPC };
        source.Rates[EconomicIndex.IGPM] = [new("2026-09", 0.4m)];
        var repository = new FakeRepository();
        repository.Values.Add(EconomicIndexValue.Create(EconomicIndex.IPCA, "2026-11", 0.3m, Now));
        var unitOfWork = new FakeUnitOfWork();
        var job = Job(source, repository, unitOfWork);

        var result = await job.RunAsync(CancellationToken.None);

        Assert.Equal("fonte fora do ar", result.Failed[EconomicIndex.INPC]);
        Assert.Equal(1, result.Added[EconomicIndex.IGPM]);
        Assert.Equal(0, result.Added[EconomicIndex.IPCA]);
        Assert.False(source.Requests.ContainsKey(EconomicIndex.IPCA)); // ja tem o mes corrente: nada a pedir
        Assert.Equal(1, unitOfWork.Resets);
    }

    // ---------- RJT-8: taxa estimada e republicacao ----------

    [Fact]
    public void LatestAccumulated_NeedsTheTwelveMostRecentMonthsInARow()
    {
        var year = Enumerable.Range(0, 12).Select(i => (new DateOnly(2025, 11, 1).AddMonths(i).ToString("yyyy-MM"), 0.5m)).ToList();

        Assert.Equal(6.1678m, RecurrenceAdjustment.LatestAccumulated(year));
        Assert.Equal(6.1678m, RecurrenceAdjustment.LatestAccumulated([("2025-01", 9m), .. year])); // so os 12 ultimos
        Assert.Null(RecurrenceAdjustment.LatestAccumulated(year.Skip(1)));
        Assert.Null(RecurrenceAdjustment.LatestAccumulated(year.Where(r => r.Item1 != "2026-03").Append(("2026-11", 0.5m))));
    }

    [Fact]
    public async Task Job_WithNewMonths_RepublishesTheRulesOfTheIndex_WithTheNewEstimatedRate()
    {
        var source = new FakeSource();
        source.Rates[EconomicIndex.IPCA] = Enumerable.Range(0, 12)
            .Select(i => new IndexMonthRate(new DateOnly(2025, 11, 1).AddMonths(i).ToString("yyyy-MM"), 0.5m)).ToList();
        var (known, unknown) = (IpcaRule(), IpcaRule());
        var recurrences = new NoRecurrences();
        recurrences.Adjusted.AddRange([known, unknown]);
        var outbox = new RecordingOutbox();
        var job = Job(source, new FakeRepository(), new FakeUnitOfWork(), recurrences, outbox,
            new FakeScopes(new Dictionary<Guid, string> { [known.AccountId] = "Personal" }), since: new DateOnly(2025, 11, 1));

        var result = await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Republished![EconomicIndex.IPCA]); // a regra sem escopo conhecido fica de fora
        var saved = Assert.IsType<RecurrenceSaved>(Assert.Single(outbox.Events));
        Assert.Equal((known.Id, "Personal", 6.1678m), (saved.RecurrenceId, saved.AccountScope, saved.Adjustment!.EstimatedRate));

        // Sem mes novo, nada sai de novo.
        await job.RunAsync(CancellationToken.None);
        Assert.Single(outbox.Events);
    }

    [Fact]
    public async Task Job_WhenTheScopesFail_DropsTheMonthsToo_AndTriesAgainNextTime()
    {
        var source = new FakeSource();
        source.Rates[EconomicIndex.IPCA] = [new("2026-10", 0.1m)];
        var recurrences = new NoRecurrences();
        recurrences.Adjusted.Add(IpcaRule());
        var unitOfWork = new FakeUnitOfWork();

        var result = await Job(source, new FakeRepository(), unitOfWork, recurrences, new RecordingOutbox(), new FakeScopes(null)).RunAsync(CancellationToken.None);

        Assert.True(result.Failed.ContainsKey(EconomicIndex.IPCA));
        Assert.Equal(1, unitOfWork.Resets);
    }

    private static Recurrence IpcaRule() => Recurrence.Create(Guid.NewGuid(), Guid.NewGuid(), TransactionType.Expense, PaymentMethod.Boleto,
        null, "Aluguel", "Moradia", 2000m, null, null, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 10), null, Now,
        new RecurrenceAdjustmentSpec(3, RecurrenceAdjustmentKind.Index, EconomicIndex.IPCA, null)).Value;

    private static FetchEconomicIndexesJob Job(
        FakeSource source, FakeRepository repository, FakeUnitOfWork? unitOfWork = null, NoRecurrences? recurrences = null,
        RecordingOutbox? outbox = null, FakeScopes? scopes = null, DateOnly? since = null) =>
        new(source, repository, recurrences ?? new NoRecurrences(), scopes ?? new FakeScopes(new Dictionary<Guid, string>()),
            outbox ?? new RecordingOutbox(), unitOfWork ?? new FakeUnitOfWork(), new IndexFetchOptions(since ?? new DateOnly(2026, 9, 1)),
            Calendar, new FixedTime(Now));

    private sealed class RecordingOutbox : IEventOutbox
    {
        public List<TransactionEvent> Events { get; } = [];

        public void Add(TransactionEvent @event) => Events.Add(@event);
    }

    /// <summary>Escopos conhecidos pelo accounts; nulo = accounts fora do ar.</summary>
    private sealed class FakeScopes(IReadOnlyDictionary<Guid, string>? scopes) : IAccountScopeDirectory
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetScopesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
            scopes is null ? throw new HttpRequestException("accounts fora do ar") : Task.FromResult(scopes);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeSource : IEconomicIndexSource
    {
        public Dictionary<EconomicIndex, IReadOnlyList<IndexMonthRate>> Rates { get; } = [];

        public Dictionary<EconomicIndex, List<(DateOnly From, DateOnly To)>> Requests { get; } = [];

        public EconomicIndex? FailOn { get; init; }

        public Task<IReadOnlyList<IndexMonthRate>> FetchAsync(EconomicIndex index, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            if (!Requests.TryGetValue(index, out var list))
            {
                Requests[index] = list = [];
            }

            list.Add((from, to));
            if (index == FailOn)
            {
                throw new HttpRequestException("fonte fora do ar");
            }

            return Task.FromResult(Rates.GetValueOrDefault(index) ?? []);
        }
    }

    private sealed class FakeRepository : IEconomicIndexRepository
    {
        public List<EconomicIndexValue> Values { get; } = [];

        public Task<string?> LastMonthAsync(EconomicIndex index, CancellationToken cancellationToken) =>
            Task.FromResult(Values.Where(v => v.Index == index).Select(v => v.Month).Order(StringComparer.Ordinal).LastOrDefault());

        public Task<IReadOnlyList<EconomicIndexValue>> ListAsync(
            EconomicIndex index, string fromMonth, string toMonth, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EconomicIndexValue>>(Values
                .Where(v => v.Index == index && string.CompareOrdinal(v.Month, fromMonth) >= 0 && string.CompareOrdinal(v.Month, toMonth) <= 0)
                .ToList());

        public void Add(EconomicIndexValue value) => Values.Add(value);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Resets { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public void Reset() => Resets++;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
