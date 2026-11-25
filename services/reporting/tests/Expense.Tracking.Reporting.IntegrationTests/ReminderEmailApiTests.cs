using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Expense.Tracking.Reporting.Application.Reminders;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>
/// NTF-8 (ADR-0009, decisao 6): e-mail dos avisos com endereco confirmado por link de uso unico, e o job do
/// resumo diario. Hoje = 2026-11-21 15:00 UTC (relogio parado do InvoicesApiFactory); a hora local vem do
/// mesmo fuso que o job usa (Sao Paulo no container; UTC numa maquina sem a base de fusos).
/// </summary>
public sealed partial class ReminderEmailApiTests(InvoicesApiFactory factory) : IClassFixture<InvoicesApiFactory>
{
    private readonly InvoicesApiFactory _factory = factory;

    private int LocalHour => TimeZoneInfo.ConvertTime(
        InvoicesApiFactory.Now,
        _factory.Services.GetRequiredService<Expense.Tracking.Reporting.Application.Invoices.InvoiceOptions>().TimeZone).Hour;

    private HttpClient WriterFor(Guid owner)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, owner.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "expenses.read expenses.write");
        return client;
    }

    private static string Address() => $"pessoa-{Guid.NewGuid():N}@exemplo.com";

    [GeneratedRegex(@"confirm-email\?token=([0-9a-f]+)")]
    private static partial Regex TokenInLink();

    private string TokenSentTo(string address) =>
        TokenInLink().Match(_factory.Emails.To(address).Last().TextBody).Groups[1].Value;

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    private async Task<int> RunDigestAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SendReminderDigestsJob>().RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Email_IsConfirmedOnlyByTheLinkToken_OnceAndForTheSameUser()
    {
        var (owner, address) = (Guid.NewGuid(), Address());
        var client = WriterFor(owner);

        var saved = await client.PutAsJsonAsync("/reports/reminder-settings/email", new { email = $"  {address.ToUpperInvariant()} ", enabled = true, sendHour = 7 });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var settings = await client.GetFromJsonAsync<JsonElement>("/reports/reminder-settings");
        var email = settings.GetProperty("email");
        Assert.Equal((address, true, false, 7), (email.GetProperty("email").GetString(), email.GetProperty("enabled").GetBoolean(),
            email.GetProperty("emailConfirmed").GetBoolean(), email.GetProperty("sendHour").GetInt32()));
        var message = Assert.Single(_factory.Emails.To(address));
        Assert.Contains("http://app.test/confirm-email?token=", message.TextBody);
        var token = TokenSentTo(address);

        var otherUser = await WriterFor(Guid.NewGuid()).PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token });
        var wrong = await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token = "abc" });
        var confirmed = await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token });
        var reused = await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, otherUser.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, "reminders.email_token_invalid"), (wrong.StatusCode, await TitleAsync(wrong)));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.True((await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);

        // Mudar os tipos de aviso nao mexe no e-mail.
        await client.PutAsJsonAsync("/reports/reminder-settings", new { leadDays = 5, invoices = true, recurrences = true, budgets = true });
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/reports/reminder-settings")).GetProperty("email").GetProperty("emailConfirmed").GetBoolean());
    }

    [Fact]
    public async Task ChangingTheAddress_ResetsTheConfirmation_AndResendInvalidatesTheOldLink()
    {
        var (owner, first, second) = (Guid.NewGuid(), Address(), Address());
        var client = WriterFor(owner);
        await client.PutAsJsonAsync("/reports/reminder-settings/email", new { email = first, enabled = true, sendHour = 8 });
        await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token = TokenSentTo(first) });

        await client.PutAsJsonAsync("/reports/reminder-settings/email", new { email = second, enabled = true, sendHour = 8 });
        var afterChange = (await client.GetFromJsonAsync<JsonElement>("/reports/reminder-settings")).GetProperty("email");
        Assert.Equal((second, false), (afterChange.GetProperty("email").GetString(), afterChange.GetProperty("emailConfirmed").GetBoolean()));
        var oldLink = TokenSentTo(second);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/reports/reminder-settings/email/resend", null)).StatusCode);
        Assert.Equal(2, _factory.Emails.To(second).Count);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token = oldLink })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token = TokenSentTo(second) })).StatusCode);
        // Nada mais esperando confirmacao: reenviar e recusado; gravar o mesmo endereco nao manda outro link.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/reports/reminder-settings/email/resend", null)).StatusCode);
        await client.PutAsJsonAsync("/reports/reminder-settings/email", new { email = second, enabled = false, sendHour = 9 });
        Assert.Equal(2, _factory.Emails.To(second).Count);
    }

    [Fact]
    public async Task InvalidInput_IsRefused()
    {
        var client = WriterFor(Guid.NewGuid());

        var cases = new (object Body, string Code)[]
        {
            (new { email = "sem-arroba", enabled = true, sendHour = 8 }, "reminders.email_invalid"),
            (new { email = "a@b", enabled = true, sendHour = 8 }, "reminders.email_invalid"),
            (new { email = (string?)null, enabled = true, sendHour = 8 }, "reminders.email_required"),
            (new { email = Address(), enabled = true, sendHour = 24 }, "reminders.send_hour_invalid"),
        };
        foreach (var (body, code) in cases)
        {
            var response = await client.PutAsJsonAsync("/reports/reminder-settings/email", body);
            Assert.Equal((HttpStatusCode.BadRequest, code), (response.StatusCode, await TitleAsync(response)));
        }
    }

    [Fact]
    public async Task Digest_GoesOnceADay_AtTheChosenHour_OnlyToConfirmedAddressesWithReminders()
    {
        var feed = _factory.EventFeed;
        async Task<(Guid Owner, string Address)> UserAsync(int sendHour, bool confirm, bool withReminders = true)
        {
            var (owner, address) = (Guid.NewGuid(), Address());
            if (withReminders)
            {
                // Fatura fechada que vence em 23/11 (2 dias) e aluguel do dia 23.
                var card = _factory.Cards.Add(owner, closingDay: 15, dueDay: 23);
                feed.CardPurchase(Guid.NewGuid(), owner, card.Id, 800m, "2026-11-10");
                feed.RecurrenceSaved(Guid.NewGuid(), owner, 2000m, "2026-11-23", description: "Aluguel");
            }

            var client = WriterFor(owner);
            await client.PutAsJsonAsync("/reports/reminder-settings/email", new { email = address, enabled = true, sendHour });
            if (confirm)
            {
                await client.PostAsJsonAsync("/reports/reminder-settings/email/confirm", new { token = TokenSentTo(address) });
            }

            return (owner, address);
        }

        var due = await UserAsync(LocalHour, confirm: true);
        var otherHour = await UserAsync((LocalHour + 3) % 24, confirm: true);
        var unconfirmed = await UserAsync(LocalHour, confirm: false);
        var nothingToSay = await UserAsync(LocalHour, confirm: true, withReminders: false);
        await _factory.DrainFeedAsync();

        await RunDigestAsync();
        var second = await RunDigestAsync();

        var digest = _factory.Emails.To(due.Address).Single(m => m.Subject.StartsWith("Expense Tracking:", StringComparison.Ordinal));
        Assert.Equal("Expense Tracking: 2 avisos de vencimento", digest.Subject);
        Assert.Contains("Aluguel: vence em 23/11", digest.TextBody);
        Assert.Contains("Fatura Cartao teste •••• 4242: vence em 23/11, falta R$", digest.TextBody);
        Assert.Contains("http://app.test/", digest.TextBody);
        Assert.Equal(0, second);
        Assert.All(new[] { otherHour, unconfirmed, nothingToSay }, u =>
            Assert.DoesNotContain(_factory.Emails.To(u.Address), m => m.Subject.StartsWith("Expense Tracking:", StringComparison.Ordinal)));
    }
}
