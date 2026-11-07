using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Accounts.IntegrationTests;

public sealed class AccountsApiTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private readonly AccountsApiFactory _factory = factory;

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

    [Fact]
    public async Task GetAccounts_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_ThenGet_ReturnsCreatedAccount()
    {
        var client = ClientFor(Guid.NewGuid());

        var create = await client.PostAsJsonAsync("/accounts", new
        {
            name = "Conta Pessoal",
            scope = "Personal",
            currency = "BRL",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var get = await client.GetAsync($"/accounts/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var dto = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Conta Pessoal", dto.GetProperty("name").GetString());
        Assert.Equal("Personal", dto.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task Accounts_AreIsolatedPerOwner()
    {
        var userA = ClientFor(Guid.NewGuid());
        var userB = ClientFor(Guid.NewGuid());

        await userA.PostAsJsonAsync("/accounts", new { name = "Conta do A", scope = "Personal" });

        var listB = await userB.GetFromJsonAsync<JsonElement>("/accounts");
        Assert.Empty(listB.EnumerateArray());

        var listA = await userA.GetFromJsonAsync<JsonElement>("/accounts");
        Assert.Single(listA.EnumerateArray());
    }

    [Fact]
    public async Task CreateAccount_Business_WithCpf_ReturnsBadRequest()
    {
        var client = ClientFor(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/accounts", new
        {
            name = "Empresa",
            scope = "Business",
            taxId = "529.982.247-25", // CPF em conta PJ
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_WithReadOnlyScope_ReturnsForbidden()
    {
        var client = ClientFor(Guid.NewGuid(), scope: "expenses.read");

        var response = await client.PostAsJsonAsync("/accounts", new { name = "X", scope = "Personal" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCreditCard_ForOwnedAccount_Succeeds()
    {
        var client = ClientFor(Guid.NewGuid());

        var acc = await (await client.PostAsJsonAsync("/accounts", new { name = "Conta", scope = "Personal" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var accountId = acc.GetProperty("id").GetString();

        var card = await client.PostAsJsonAsync("/credit-cards", new
        {
            accountId,
            label = "Nubank",
            brand = "Mastercard",
            lastFourDigits = "1234",
            creditLimit = 5000,
            closingDay = 10,
            dueDay = 17,
        });

        Assert.Equal(HttpStatusCode.Created, card.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/credit-cards");
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task CreateCreditCard_ForUnknownAccount_ReturnsNotFound()
    {
        var client = ClientFor(Guid.NewGuid());

        var card = await client.PostAsJsonAsync("/credit-cards", new
        {
            accountId = Guid.NewGuid(),
            label = "Fantasma",
            brand = "Visa",
            lastFourDigits = "0000",
            creditLimit = 100,
            closingDay = 1,
            dueDay = 10,
        });

        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);
    }

    [Fact]
    public async Task PaymentMethods_ReturnsCatalog()
    {
        var client = ClientFor(Guid.NewGuid());

        var methods = await client.GetFromJsonAsync<JsonElement>("/payment-methods");
        var codes = methods.EnumerateArray().Select(m => m.GetProperty("code").GetString()).ToArray();

        Assert.Contains("pix", codes);
        Assert.Contains("credit_card", codes);
        Assert.Contains("boleto", codes);
    }
}
