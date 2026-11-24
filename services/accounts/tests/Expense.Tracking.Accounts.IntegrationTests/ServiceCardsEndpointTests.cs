using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>
/// NTF-7 (ADR-0009, decisao 6): <c>GET /internal/credit-cards?ownerUserId=</c>, cartoes de um dono com
/// ciclo e escopo da conta, so para token de servico com <c>accounts.cards.read</c>.
/// </summary>
public sealed class ServiceCardsEndpointTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private const string ServiceScope = "accounts.cards.read";
    private readonly AccountsApiFactory _factory = factory;

    private HttpClient ClientFor(Guid sub, string? scope = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub.ToString());
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private async Task<Guid> CreateAccountAsync(Guid owner, string scope, string? taxId = null)
    {
        var response = await ClientFor(owner).PostAsJsonAsync("/accounts", new { name = $"Conta {scope}", scope, taxId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCardAsync(Guid owner, Guid accountId, string label, int closingDay, int dueDay)
    {
        var response = await ClientFor(owner).PostAsJsonAsync("/credit-cards", new
        {
            accountId, label, brand = "Visa", lastFourDigits = "4321", creditLimit = 3000, closingDay, dueDay,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ServiceToken_GetsTheOwnersCards_ActiveOrNot_WithCycleAndAccountScope()
    {
        var owner = Guid.NewGuid();
        var personal = await CreateAccountAsync(owner, "Personal");
        var business = await CreateAccountAsync(owner, "Business", taxId: "11222333000181");
        var pf = await CreateCardAsync(owner, personal, "PF", 10, 17);
        var pj = await CreateCardAsync(owner, business, "PJ", 5, 15);
        Assert.Equal(HttpStatusCode.NoContent, (await ClientFor(owner).DeleteAsync($"/credit-cards/{pj}")).StatusCode);
        // Cartao de outro dono nao vem.
        var stranger = Guid.NewGuid();
        await CreateCardAsync(stranger, await CreateAccountAsync(stranger, "Personal"), "Outro", 1, 8);

        var response = await ClientFor(Guid.NewGuid(), ServiceScope).GetAsync($"/internal/credit-cards?ownerUserId={owner}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cards = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetGuid());
        Assert.Equal(new[] { pf, pj }.Order(), cards.Keys.Order());
        Assert.Equal((personal, "Personal", 10, 17, true), (cards[pf].GetProperty("accountId").GetGuid(),
            cards[pf].GetProperty("accountScope").GetString(), cards[pf].GetProperty("closingDay").GetInt32(),
            cards[pf].GetProperty("dueDay").GetInt32(), cards[pf].GetProperty("isActive").GetBoolean()));
        Assert.Equal(("Business", false), (cards[pj].GetProperty("accountScope").GetString(), cards[pj].GetProperty("isActive").GetBoolean()));
    }

    [Fact]
    public async Task WithoutTheServiceScope_IsForbidden_AndWithoutOwnerIsBadRequest()
    {
        var owner = Guid.NewGuid();
        await CreateCardAsync(owner, await CreateAccountAsync(owner, "Personal"), "PF", 10, 17);

        var userToken = await ClientFor(owner).GetAsync($"/internal/credit-cards?ownerUserId={owner}");
        var otherServiceScope = await ClientFor(Guid.NewGuid(), "accounts.scopes.read").GetAsync($"/internal/credit-cards?ownerUserId={owner}");
        var anonymous = await _factory.CreateClient().GetAsync($"/internal/credit-cards?ownerUserId={owner}");
        var noOwner = await ClientFor(Guid.NewGuid(), ServiceScope).GetAsync("/internal/credit-cards");

        Assert.Equal(HttpStatusCode.Forbidden, userToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherServiceScope.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, "service_cards.owner_required"),
            (noOwner.StatusCode, (await noOwner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()));
    }
}
