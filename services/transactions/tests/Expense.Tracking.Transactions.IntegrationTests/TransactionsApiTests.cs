using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Transactions.IntegrationTests;

public sealed class TransactionsApiTests(TransactionsApiFactory factory) : IClassFixture<TransactionsApiFactory>
{
    private readonly TransactionsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid userId, string? scope = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, userId.ToString());
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }
        return client;
    }

    private static object CashTransaction(Guid accountId, string category = "Alimentacao", decimal amount = 100m) => new
    {
        accountId,
        type = "Expense",
        paymentMethod = "Pix",
        description = "Almoco",
        category,
        amount,
        currency = "BRL",
        occurredOn = "2026-01-15",
    };

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidPix_ReturnsCreated()
    {
        var client = ClientFor(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/transactions", CashTransaction(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pix", dto.GetProperty("paymentMethod").GetString());
        Assert.Equal(100m, dto.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Create_WithReadOnlyScope_ReturnsForbidden()
    {
        var client = ClientFor(Guid.NewGuid(), scope: "expenses.read");

        var response = await client.PostAsJsonAsync("/transactions", CashTransaction(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_CreditCardWithoutCardId_ReturnsBadRequest()
    {
        var client = ClientFor(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/transactions", new
        {
            accountId = Guid.NewGuid(),
            paymentMethod = "CreditCard",
            description = "Compra",
            category = "Compras",
            amount = 200m,
            currency = "BRL",
            occurredOn = "2026-01-15",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ForUnknownAccount_ReturnsNotFound()
    {
        var client = ClientFor(Guid.NewGuid());
        var unknownAccount = Guid.NewGuid();
        _factory.AccountsClient.InvalidAccounts.Add(unknownAccount);

        var response = await client.PostAsJsonAsync("/transactions", CashTransaction(unknownAccount));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_CreditCard_WithUnknownCard_ReturnsNotFound()
    {
        var client = ClientFor(Guid.NewGuid());
        var card = Guid.NewGuid();
        _factory.AccountsClient.InvalidCards.Add(card);

        var response = await client.PostAsJsonAsync("/transactions", new
        {
            accountId = Guid.NewGuid(),
            paymentMethod = "CreditCard",
            creditCardId = card,
            description = "Compra",
            category = "Compras",
            amount = 200m,
            currency = "BRL",
            occurredOn = "2026-01-15",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_IsIsolatedPerOwner_AndFiltersByAccount()
    {
        var userA = ClientFor(Guid.NewGuid());
        var userB = ClientFor(Guid.NewGuid());
        var accountX = Guid.NewGuid();

        await userA.PostAsJsonAsync("/transactions", CashTransaction(accountX, "Transporte"));
        await userA.PostAsJsonAsync("/transactions", CashTransaction(Guid.NewGuid(), "Lazer"));
        await userB.PostAsJsonAsync("/transactions", CashTransaction(Guid.NewGuid(), "Mercado"));

        var listB = await userB.GetFromJsonAsync<JsonElement>("/transactions");
        Assert.Single(listB.EnumerateArray());

        var listA = await userA.GetFromJsonAsync<JsonElement>("/transactions");
        Assert.Equal(2, listA.EnumerateArray().Count());

        var filtered = await userA.GetFromJsonAsync<JsonElement>($"/transactions?accountId={accountX}");
        Assert.Single(filtered.EnumerateArray());
        Assert.Equal("Transporte", filtered.EnumerateArray().First().GetProperty("category").GetString());
    }

    [Fact]
    public async Task Create_CreditCardInInstallments_ReturnsTheInstallments_AndKeepsTheTotal()
    {
        var client = ClientFor(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/transactions", Purchase("CreditCard", Guid.NewGuid(), installments: 10));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((10, 1000m), (dto.GetProperty("installments").GetInt32(), dto.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task Create_WithoutInstallments_IsInFull()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/transactions", Purchase("CreditCard", Guid.NewGuid()));

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("installments").GetInt32());
    }

    [Theory]
    [InlineData("CreditCard", 25, "transaction.installments_invalid")]
    [InlineData("CreditCard", 0, "transaction.installments_invalid")]
    [InlineData("Pix", 3, "transaction.installments_not_allowed")]
    public async Task Create_WithInvalidInstallments_ReturnsBadRequest(string method, int installments, string code)
    {
        var card = method == "CreditCard" ? Guid.NewGuid() : (Guid?)null;

        var response = await ClientFor(Guid.NewGuid()).PostAsJsonAsync("/transactions", Purchase(method, card, installments));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_IncomeWithNotes_IsReturnedAsIncome_ButNeverInInstallments()
    {
        var client = ClientFor(Guid.NewGuid());

        var income = await client.PostAsJsonAsync("/transactions", new
        {
            accountId = Guid.NewGuid(), type = "Income", paymentMethod = "Pix", description = "Cliente A",
            category = "Servicos", amount = 5000m, currency = "BRL", occurredOn = "2026-11-15", notes = "  NF 12  ",
        });
        var dto = await income.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((HttpStatusCode.Created, "Income", "NF 12"),
            (income.StatusCode, dto.GetProperty("type").GetString(), dto.GetProperty("notes").GetString()));

        var split = await client.PostAsJsonAsync("/transactions", Purchase("CreditCard", Guid.NewGuid(), 3, type: "Income"));
        Assert.Equal((HttpStatusCode.BadRequest, "transaction.installments_not_allowed"),
            (split.StatusCode, (await split.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()));
    }

    private static object Purchase(string paymentMethod, Guid? creditCardId, int? installments = null, string type = "Expense") => new
    {
        accountId = Guid.NewGuid(),
        type,
        paymentMethod,
        creditCardId,
        description = "Notebook",
        category = "Eletronicos",
        amount = 1000m,
        currency = "BRL",
        occurredOn = "2026-11-15",
        installments,
    };
}
