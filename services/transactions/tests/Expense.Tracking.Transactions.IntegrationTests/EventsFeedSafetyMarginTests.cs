using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// EVT-1: o feed so serve eventos mais antigos que a margem de seguranca. Classe e banco proprios:
/// o relogio controlado grava eventos com horario que, num banco compartilhado, travaria o feed
/// dos outros testes (o feed para no primeiro evento recente demais, por desenho).
/// </summary>
public sealed class EventsFeedSafetyMarginTests : IClassFixture<EventsFeedSafetyMarginTests.MarginFactory>
{
    private readonly MarginFactory _factory;

    public EventsFeedSafetyMarginTests(MarginFactory factory) => _factory = factory;

    [Fact]
    public async Task Feed_HidesEventsNewerThanTheSafetyMargin_UntilTheMarginPasses()
    {
        var user = _factory.CreateClient();
        user.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, Guid.NewGuid().ToString());
        var service = _factory.CreateClient();
        service.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "expense-tracking-service");
        service.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "events.read");

        var create = await user.PostAsJsonAsync("/transactions", new
        {
            accountId = Guid.NewGuid(),
            type = "Expense",
            paymentMethod = "Pix",
            description = "Almoco",
            category = "Alimentacao",
            amount = 10m,
            currency = "BRL",
            occurredOn = "2026-01-15",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        Assert.Empty(await ReadEventsAsync(service));

        _factory.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Empty(await ReadEventsAsync(service));

        _factory.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Single(await ReadEventsAsync(service));
    }

    private static async Task<JsonElement[]> ReadEventsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/events?after=0&limit=100");
        return page.GetProperty("events").EnumerateArray().ToArray();
    }

    /// <summary>Margem de 5 s e relogio controlado pelo teste.</summary>
    public sealed class MarginFactory : TransactionsApiFactory
    {
        public ManualTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        protected override void ConfigureTestSettings(IDictionary<string, string?> settings) =>
            settings["Events:FeedSafetyMarginSeconds"] = "5";

        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.AddSingleton<TimeProvider>(Clock);
    }

    public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
