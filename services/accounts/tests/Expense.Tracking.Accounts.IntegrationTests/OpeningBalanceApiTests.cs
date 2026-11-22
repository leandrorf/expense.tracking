using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>
/// INV-13: saldo da conta no fim de um dia (<c>PUT/DELETE /accounts/{id}/opening-balance</c>),
/// devolvido em toda leitura da conta para o reporting projetar o saldo.
/// </summary>
public sealed class OpeningBalanceApiTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private readonly AccountsApiFactory _factory = factory;

    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

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

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> TitleAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("title").GetString();

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static async Task<string> CreateAccountAsync(HttpClient client)
    {
        var created = await Json(await client.PostAsJsonAsync("/accounts", new { name = "Conta saldo", scope = "Personal" }));
        return created.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task OpeningBalance_IsSet_ShowsInTheList_AndCanBeCleared()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAccountAsync(client);
        var created = await client.GetFromJsonAsync<JsonElement>($"/accounts/{id}");
        Assert.Equal(JsonValueKind.Null, created.GetProperty("openingBalance").ValueKind);

        var set = await client.PutAsJsonAsync($"/accounts/{id}/opening-balance",
            new { amount = 1234.56m, date = Iso(TodayUtc.AddDays(-5)) });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var listed = (await client.GetFromJsonAsync<JsonElement>("/accounts")).EnumerateArray().Single();
        Assert.Equal((1234.56m, Iso(TodayUtc.AddDays(-5))),
            (listed.GetProperty("openingBalance").GetDecimal(), listed.GetProperty("openingBalanceOn").GetString()));

        var cleared = await client.DeleteAsync($"/accounts/{id}/opening-balance");

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var read = await client.GetFromJsonAsync<JsonElement>($"/accounts/{id}");
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null),
            (read.GetProperty("openingBalance").ValueKind, read.GetProperty("openingBalanceOn").ValueKind));
    }

    [Fact]
    public async Task OpeningBalance_RejectsFutureDates_InvalidAmounts_AndMissingFields()
    {
        var client = ClientFor(Guid.NewGuid());
        var id = await CreateAccountAsync(client);

        var future = await client.PutAsJsonAsync($"/accounts/{id}/opening-balance", new { amount = 10m, date = Iso(TodayUtc.AddDays(2)) });
        var cents = await client.PutAsJsonAsync($"/accounts/{id}/opening-balance", new { amount = 10.001m, date = Iso(TodayUtc) });
        var missing = await client.PutAsJsonAsync($"/accounts/{id}/opening-balance", new { amount = 10m });

        Assert.Equal((HttpStatusCode.BadRequest, "account.opening_balance_date_future"), (future.StatusCode, await TitleAsync(future)));
        Assert.Equal((HttpStatusCode.BadRequest, "account.opening_balance_invalid"), (cents.StatusCode, await TitleAsync(cents)));
        Assert.Equal((HttpStatusCode.BadRequest, "account.opening_balance_required"), (missing.StatusCode, await TitleAsync(missing)));
    }

    [Fact]
    public async Task OpeningBalance_OfSomeoneElsesAccount_IsNotFound_AndReadScopeIsForbidden()
    {
        var id = await CreateAccountAsync(ClientFor(Guid.NewGuid()));
        var body = new { amount = 10m, date = Iso(TodayUtc) };

        var other = await ClientFor(Guid.NewGuid()).PutAsJsonAsync($"/accounts/{id}/opening-balance", body);
        var readOnly = await ClientFor(Guid.NewGuid(), scope: "expenses.read").PutAsJsonAsync($"/accounts/{id}/opening-balance", body);

        Assert.Equal((HttpStatusCode.NotFound, "account.not_found"), (other.StatusCode, await TitleAsync(other)));
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
    }
}
