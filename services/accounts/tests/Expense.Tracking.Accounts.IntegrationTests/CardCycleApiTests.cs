using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>
/// CYC-3 (ADR-0012, decisoes 3 a 5 e 8): troca de ciclo do cartao por <c>PUT /credit-cards/{id}/cycle</c>, desfazer a
/// pendente e o historico na leitura (rota do usuario e endpoint interno). Os meses sao calculados a partir de hoje.
/// </summary>
public sealed class CardCycleApiTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private readonly AccountsApiFactory _factory = factory;

    /// <summary>Cartao que fecha dia 10: a fatura aberta e a deste mes ate o dia 10, depois a do mes seguinte.</summary>
    private static readonly DateOnly OpenDueMonth = OpenMonth();

    private static DateOnly OpenMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var month = new DateOnly(today.Year, today.Month, 1);
        return today.Day <= 10 ? month : month.AddMonths(1);
    }

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

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

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<(HttpStatusCode, string?)> ProblemOf(HttpResponseMessage response) =>
        (response.StatusCode, (await Json(response)).GetProperty("title").GetString());

    private async Task<(HttpClient Client, Guid Owner, Guid CardId)> CardAsync()
    {
        var owner = Guid.NewGuid();
        var client = ClientFor(owner);
        var account = await Json(await client.PostAsJsonAsync("/accounts", new { name = "Conta", scope = "Personal" }));
        var card = await Json(await client.PostAsJsonAsync("/credit-cards", new
        {
            accountId = account.GetProperty("id").GetGuid(), label = "Nubank", brand = "Visa", lastFourDigits = "1234",
            creditLimit = 5000, closingDay = 10, dueDay = 17,
        }));
        return (client, owner, card.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ChangeCycle_FromTheNextInvoice_IsKeptWithTheHistory_AndCanBeUndoneWhilePending()
    {
        var (client, owner, cardId) = await CardAsync();
        var from = Month(OpenDueMonth.AddMonths(1));

        var changed = await client.PutAsJsonAsync($"/credit-cards/{cardId}/cycle", new { closingDay = 25, dueDay = 5, fromDueMonth = from });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var card = await Json(changed);
        // O ciclo de hoje continua o antigo; o historico traz os dois.
        Assert.Equal((10, 17), (card.GetProperty("closingDay").GetInt32(), card.GetProperty("dueDay").GetInt32()));
        Assert.Equal([(null, 10, 17), (from, 25, 5)], card.GetProperty("cycles").EnumerateArray()
            .Select(c => (c.GetProperty("fromDueMonth").GetString(), c.GetProperty("closingDay").GetInt32(), c.GetProperty("dueDay").GetInt32())));
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>($"/credit-cards/{cardId}")).GetProperty("cycles").GetArrayLength());

        // O endpoint interno (resumo por e-mail) tambem traz o historico.
        var service = ClientFor(Guid.NewGuid(), "accounts.cards.read");
        var serviceCard = (await service.GetFromJsonAsync<JsonElement>($"/internal/credit-cards?ownerUserId={owner}")).EnumerateArray().Single();
        Assert.Equal(2, serviceCard.GetProperty("cycles").GetArrayLength());

        var undone = await client.DeleteAsync($"/credit-cards/{cardId}/cycle/{from}");
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal(1, (await Json(undone)).GetProperty("cycles").GetArrayLength());
        Assert.Equal((HttpStatusCode.NotFound, "card.cycle_not_found"), await ProblemOf(await client.DeleteAsync($"/credit-cards/{cardId}/cycle/{from}")));
    }

    [Fact]
    public async Task ChangeCycle_RefusesClosedInvoicesInvalidMonthsAndCardsOfOthers()
    {
        var (client, _, cardId) = await CardAsync();
        var url = $"/credit-cards/{cardId}/cycle";

        Assert.Equal((HttpStatusCode.BadRequest, "card.cycle_change_invalid"),
            await ProblemOf(await client.PutAsJsonAsync(url, new { closingDay = 25, dueDay = 5, fromDueMonth = Month(OpenDueMonth.AddMonths(-1)) })));
        Assert.Equal((HttpStatusCode.BadRequest, "card.month_invalid"),
            await ProblemOf(await client.PutAsJsonAsync(url, new { closingDay = 25, dueDay = 5, fromDueMonth = "marco" })));
        Assert.Equal((HttpStatusCode.BadRequest, "card.day_invalid"),
            await ProblemOf(await client.PutAsJsonAsync(url, new { closingDay = 30, dueDay = 5, fromDueMonth = Month(OpenDueMonth.AddMonths(1)) })));
        Assert.Equal((HttpStatusCode.NotFound, "card.not_found"),
            await ProblemOf(await ClientFor(Guid.NewGuid()).PutAsJsonAsync(url, new { closingDay = 25, dueDay = 5, fromDueMonth = Month(OpenDueMonth.AddMonths(1)) })));
    }

    // ---------- CYC-2: dados do cartao ----------

    [Fact]
    public async Task Edit_CorrectsNameAndLimit_AndRefusesInvalidDataAndCardsOfOthers()
    {
        var (client, _, cardId) = await CardAsync();

        var edited = await client.PutAsJsonAsync($"/credit-cards/{cardId}",
            new { label = "Nubank Black", brand = "Mastercard", lastFourDigits = "9876", creditLimit = 8000 });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var card = await client.GetFromJsonAsync<JsonElement>($"/credit-cards/{cardId}");
        Assert.Equal(("Nubank Black", "Mastercard", "9876", 8000m, 10), (card.GetProperty("label").GetString(), card.GetProperty("brand").GetString(),
            card.GetProperty("lastFourDigits").GetString(), card.GetProperty("creditLimit").GetDecimal(), card.GetProperty("closingDay").GetInt32()));

        Assert.Equal((HttpStatusCode.BadRequest, "card.last_four_invalid"), await ProblemOf(await client.PutAsJsonAsync($"/credit-cards/{cardId}",
            new { label = "Nubank", brand = "Visa", lastFourDigits = "12", creditLimit = 8000 })));
        Assert.Equal((HttpStatusCode.NotFound, "card.not_found"), await ProblemOf(await ClientFor(Guid.NewGuid()).PutAsJsonAsync($"/credit-cards/{cardId}",
            new { label = "Nubank", brand = "Visa", lastFourDigits = "1234", creditLimit = 8000 })));

        await client.DeleteAsync($"/credit-cards/{cardId}");
        Assert.Equal((HttpStatusCode.BadRequest, "card.inactive"), await ProblemOf(await client.PutAsJsonAsync($"/credit-cards/{cardId}",
            new { label = "Nubank", brand = "Visa", lastFourDigits = "1234", creditLimit = 8000 })));
    }
}
