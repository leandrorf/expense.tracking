using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Transactions;
using Expense.Tracking.Transactions.Infrastructure.Auth;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// EVT-4: lancamentos anteriores ao outbox ganham um <see cref="TransactionAccountScopeResolved"/>
/// com o escopo vindo do accounts. Factory propria: as contagens dependem de um banco so deste teste.
/// </summary>
public sealed class AccountScopeBackfillTests(AccountScopeBackfillTests.Factory factory)
    : IClassFixture<AccountScopeBackfillTests.Factory>
{
    private readonly Factory _factory = factory;

    [Fact]
    public async Task Backfill_ResolvesTheScopeOfLegacyTransactions_Once()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var businessAccount = Guid.NewGuid();
        var unknownAccount = Guid.NewGuid();

        var legacy = await AddLegacyAsync(db, businessAccount);
        var legacyWithUnknownAccount = await AddLegacyAsync(db, unknownAccount);
        var legacyDeleted = await AddLegacyAsync(db, businessAccount);
        await OutboxBackfill.RunAsync(db, TimeProvider.System);
        db.Transactions.Remove(legacyDeleted);
        await db.SaveChangesAsync();

        // Lancamento criado pela API ja nasce com escopo no evento: fora do backfill.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, Guid.NewGuid().ToString());
        var created = await client.PostAsJsonAsync("/transactions", new
        {
            accountId = businessAccount, type = "Expense", paymentMethod = "Pix", description = "Atual",
            category = "Mercado", amount = 10m, currency = "BRL", occurredOn = "2026-11-19",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var directory = new FakeAccountScopeDirectory { Scopes = { [businessAccount] = "Business" } };
        var first = await AccountScopeBackfill.RunAsync(db, directory, TimeProvider.System);
        var second = await AccountScopeBackfill.RunAsync(db, directory, TimeProvider.System);

        Assert.Equal(new AccountScopeBackfillResult(1, 1), first);
        // A conta desconhecida volta a ser consultada (pode existir depois), mas nada se repete.
        Assert.Equal(new AccountScopeBackfillResult(0, 1), second);
        // Conjunto de contas consultadas (a ordem segue os GUIDs, que sao aleatorios).
        Assert.Equal(new[] { businessAccount, unknownAccount }.Order(), directory.Requested[0].Order());

        var resolved = Assert.Single(await db.OutboxEvents.AsNoTracking()
            .Where(e => e.EventType == TransactionAccountScopeResolved.Name).ToListAsync());
        Assert.Equal(legacy.Id, resolved.AggregateId);
        Assert.Equal(legacy.OwnerUserId, resolved.OwnerUserId);
        var payload = JsonDocument.Parse(resolved.Payload).RootElement;
        Assert.Equal(legacy.Id, payload.GetProperty("transactionId").GetGuid());
        Assert.Equal(businessAccount, payload.GetProperty("accountId").GetGuid());
        Assert.Equal("Business", payload.GetProperty("accountScope").GetString());
        Assert.DoesNotContain(legacyWithUnknownAccount.Id, new[] { resolved.AggregateId });
    }

    [Fact]
    public async Task Directory_AsksTheAccountsInBatchesOf100_WithTheServiceToken()
    {
        // Client registrado pela aplicacao; so a rede (handler primario) e o token sao trocados.
        var handler = new RecordingHandler();
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IServiceTokenProvider>(new FixedTokenProvider("service-token"));
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        }));
        var ids = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToArray();

        await using var scope = factory.Services.CreateAsyncScope();
        var scopes = await scope.ServiceProvider.GetRequiredService<IAccountScopeDirectory>().GetScopesAsync(ids, CancellationToken.None);

        Assert.Equal([100, 50], handler.Requests.Select(r => r.Ids.Length));
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("/internal/account-scopes", r.Path);
            Assert.Equal("Bearer service-token", r.Authorization);
        });
        Assert.Equal(150, scopes.Count);
        Assert.All(scopes.Values, s => Assert.Equal("Business", s));
    }

    private static async Task<Transaction> AddLegacyAsync(TransactionsDbContext db, Guid accountId)
    {
        var transaction = Transaction.Create(
            Guid.NewGuid(), accountId, TransactionType.Expense, PaymentMethod.Cash, null,
            "Antigo", "Mercado", 80m, "BRL", new DateOnly(2025, 12, 1), null, DateTimeOffset.UtcNow).Value;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    public sealed class Factory : TransactionsApiFactory;

    private sealed class FakeAccountScopeDirectory : IAccountScopeDirectory
    {
        public Dictionary<Guid, string> Scopes { get; } = [];

        public List<Guid[]> Requested { get; } = [];

        public Task<IReadOnlyDictionary<Guid, string>> GetScopesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
        {
            Requested.Add(accountIds.ToArray());
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                accountIds.Where(Scopes.ContainsKey).ToDictionary(id => id, id => Scopes[id]));
        }
    }

    private sealed class FixedTokenProvider(string token) : IServiceTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    /// <summary>Faz o papel do accounts: devolve "Business" para cada id pedido.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, Guid[] Ids, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ids = request.RequestUri!.Query.TrimStart('?').Split('&')
                .Select(pair => Guid.Parse(pair["ids=".Length..]))
                .ToArray();
            Requests.Add((request.RequestUri.AbsolutePath, ids, request.Headers.Authorization?.ToString()));

            var body = JsonSerializer.Serialize(ids.Select(id => new { accountId = id, scope = "Business" }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
