using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>EVT-4: endpoint interno com o escopo PF/PJ das contas, so para token de servico.</summary>
public sealed class AccountScopesEndpointTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private const string ServiceScope = "accounts.scopes.read";
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

    [Fact]
    public async Task WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync($"/internal/account-scopes?ids={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserToken_IsForbidden_EvenForItsOwnAccount()
    {
        var owner = Guid.NewGuid();
        var account = await CreateAccountAsync(owner, "Personal");

        var response = await ClientFor(owner).GetAsync($"/internal/account-scopes?ids={account}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_GetsTheScopesOfAccountsFromDifferentOwners_AndSkipsUnknownIds()
    {
        var personal = await CreateAccountAsync(Guid.NewGuid(), "Personal");
        var business = await CreateAccountAsync(Guid.NewGuid(), "Business", taxId: "11222333000181");
        var unknown = Guid.NewGuid();

        var response = await ClientFor(Guid.NewGuid(), ServiceScope)
            .GetAsync($"/internal/account-scopes?ids={personal}&ids={business}&ids={unknown}&ids={personal}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scopes = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .ToDictionary(e => e.GetProperty("accountId").GetGuid(), e => e.GetProperty("scope").GetString());
        Assert.Equal(2, scopes.Count);
        Assert.Equal("Personal", scopes[personal]);
        Assert.Equal("Business", scopes[business]);

        // So o escopo: nenhum outro dado da conta (nome, dono, documento) sai deste endpoint.
        var first = (await (await ClientFor(Guid.NewGuid(), ServiceScope)
            .GetAsync($"/internal/account-scopes?ids={business}")).Content.ReadFromJsonAsync<JsonElement>())[0];
        Assert.Equal(["accountId", "scope"], first.EnumerateObject().Select(p => p.Name).Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task IdCountOutsideTheLimit_ReturnsBadRequest(int count)
    {
        var query = string.Join("&", Enumerable.Range(0, count).Select(_ => $"ids={Guid.NewGuid()}"));

        var response = await ClientFor(Guid.NewGuid(), ServiceScope).GetAsync($"/internal/account-scopes?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreateAccountAsync(Guid owner, string scope, string? taxId = null)
    {
        var response = await ClientFor(owner).PostAsJsonAsync("/accounts", new { name = $"Conta {scope}", scope, taxId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
