using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// OBS-2. Criterio de aceite: dado um lancamento de gasto, quando o transactions chama o
/// accounts, entao o trace mostra o span pai e o filho correlacionados.
/// A request chega com um <c>traceparent</c> como o que o gateway (YARP) envia; o teste confere
/// que o span do transactions continua esse trace e que a chamada ao accounts e filha dele,
/// tanto nos spans exportados quanto no header que o accounts de fato recebeu.
/// </summary>
public sealed class DistributedTracingTests(DistributedTracingTests.TracingApiFactory factory)
    : IClassFixture<DistributedTracingTests.TracingApiFactory>
{
    private readonly TracingApiFactory _factory = factory;

    [Fact]
    public async Task CreateTransaction_AccountsCall_IsChildSpanOfTheIncomingTrace()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var gatewaySpanId = ActivitySpanId.CreateRandom();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, Guid.NewGuid().ToString());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                accountId = Guid.NewGuid(),
                type = "Expense",
                paymentMethod = "Pix",
                description = "Almoco",
                category = "Alimentacao",
                amount = 42m,
                currency = "BRL",
                occurredOn = "2026-01-15",
            }),
        };
        request.Headers.Add("traceparent", $"00-{traceId}-{gatewaySpanId}-01");

        var response = await client.SendAsync(request);
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Esperado 201, veio {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var (serverSpan, accountsCallSpan) = await WaitForSpansAsync(traceId);

        // O span do transactions continua o trace do gateway (pai = span do gateway)...
        Assert.Equal(gatewaySpanId, serverSpan.ParentSpanId);
        // ...e a chamada ao accounts e filha do span do transactions, no mesmo trace.
        Assert.Equal(serverSpan.SpanId, accountsCallSpan.ParentSpanId);

        // O accounts recebeu o contexto W3C apontando para o span da chamada.
        var expectedHeader = $"00-{traceId}-{accountsCallSpan.SpanId}-01";
        Assert.Contains(expectedHeader, _factory.AccountsServer.ReceivedTraceParents);
    }

    private async Task<(Activity Server, Activity Client)> WaitForSpansAsync(ActivityTraceId traceId)
    {
        // O span do servidor so e exportado quando a request termina de ser descartada,
        // o que pode acontecer logo depois de o cliente receber a resposta.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var spans = _factory.SnapshotSpans().Where(s => s.TraceId == traceId).ToList();

            var server = spans.FirstOrDefault(s =>
                s.Kind == ActivityKind.Server && Equals(s.GetTagItem("url.path"), "/transactions"));
            var accountsCall = spans.FirstOrDefault(s =>
                s.Kind == ActivityKind.Client
                && (s.GetTagItem("url.full") as string)?.Contains("/accounts/", StringComparison.Ordinal) == true);

            if (server is not null && accountsCall is not null)
            {
                return (server, accountsCall);
            }

            if (DateTime.UtcNow > deadline)
            {
                var seen = string.Join(", ", spans.Select(s => $"{s.Kind}:{s.DisplayName}"));
                Assert.Fail($"Spans esperados nao exportados para o trace {traceId}. Vistos: [{seen}]");
            }

            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Usa o <c>AccountsHttpClient</c> real (com a pilha HTTP do .NET) apontando para um
    /// accounts falso, e captura os spans num exporter em memoria.
    /// </summary>
    public sealed class TracingApiFactory : TransactionsApiFactory
    {
        private readonly List<Activity> _exportedSpans = [];

        public StubAccountsServer AccountsServer { get; } = new();

        public IReadOnlyList<Activity> SnapshotSpans()
        {
            lock (_exportedSpans)
            {
                return [.. _exportedSpans];
            }
        }

        protected override bool UseFakeAccountsClient => false;

        protected override void ConfigureTestSettings(IDictionary<string, string?> settings) =>
            settings["Services:AccountsBaseUrl"] = AccountsServer.BaseUrl;

        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddProcessor(new SimpleActivityExportProcessor(new LockingListExporter(_exportedSpans))));

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await AccountsServer.DisposeAsync();
        }
    }

    /// <summary>Exporter em memoria com lock, para o teste ler a lista enquanto spans chegam.</summary>
    private sealed class LockingListExporter(List<Activity> target) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (target)
            {
                foreach (var activity in batch)
                {
                    target.Add(activity);
                }
            }

            return ExportResult.Success;
        }
    }
}
