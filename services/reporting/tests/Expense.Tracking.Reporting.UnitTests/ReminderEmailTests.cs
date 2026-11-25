using Expense.Tracking.Reporting.Application.Reminders;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>NTF-8 (ADR-0009, decisao 6): regras do e-mail dos avisos.</summary>
public sealed class ReminderEmailTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly DateOnly Today = new(2026, 11, 21);

    private static ReminderSettings Confirmed(int sendHour = 8)
    {
        var settings = ReminderSettings.Default(Guid.NewGuid());
        settings.UpdateEmail("pessoa@exemplo.com", enabled: true, sendHour, EmailTokens.HashOf("tok"), Now);
        settings.ConfirmEmail(EmailTokens.HashOf("tok"), Now);
        return settings;
    }

    [Fact]
    public void Digest_IsDueOncePerDay_AtTheChosenHour_AndOnlyWithANewKey()
    {
        var settings = Confirmed(sendHour: 8);

        Assert.False(settings.IsDigestDue(Today, 7));
        Assert.True(settings.IsDigestDue(Today, 8));
        Assert.True(settings.HasNewKeys(["a", "b"]));

        settings.RecordDigestSent(Today, ["a", "b"], Now);

        Assert.False(settings.IsDigestDue(Today, 8));
        Assert.True(settings.IsDigestDue(Today.AddDays(1), 8));
        // Amanha, os mesmos avisos (ou menos) nao geram outro e-mail; um novo gera.
        Assert.False(settings.HasNewKeys(["b"]));
        Assert.True(settings.HasNewKeys(["b", "c"]));

        // Outro endereco, confirmado: ele ainda nao recebeu nada, entao o resumo de hoje sai de novo.
        settings.UpdateEmail("outra@exemplo.com", enabled: true, 8, EmailTokens.HashOf("t2"), Now);
        settings.ConfirmEmail(EmailTokens.HashOf("t2"), Now);
        Assert.True(settings.IsDigestDue(Today, 8));
        Assert.True(settings.HasNewKeys(["a"]));
    }

    [Fact]
    public void ConfirmationToken_ExpiresIn24Hours_AndUnconfirmedOrDisabledNeverSends()
    {
        var settings = ReminderSettings.Default(Guid.NewGuid());
        settings.UpdateEmail("pessoa@exemplo.com", enabled: true, 8, EmailTokens.HashOf("tok"), Now);

        Assert.False(settings.IsDigestDue(Today, 8));
        Assert.Equal("reminders.email_token_invalid", settings.ConfirmEmail(EmailTokens.HashOf("tok"), Now.AddHours(25)).Error.Code);

        var disabled = Confirmed();
        disabled.UpdateEmail("pessoa@exemplo.com", enabled: false, 8, EmailTokens.HashOf("x"), Now);
        Assert.False(disabled.IsDigestDue(Today, 8));
        Assert.True(disabled.EmailConfirmed); // mesmo endereco: a confirmacao fica
    }

    [Fact]
    public void Digest_FormatsMoneyAndDatesTheBrazilianWay_WithoutDependingOnTheCulture()
    {
        Reminder[] reminders =
        [
            new(ReminderKind.InvoiceOverdue, "k1", ReminderUrgency.Overdue, new DateOnly(2026, 11, 5), 1234.5m, "Fatura Nubank •••• 1234"),
            new(ReminderKind.RecurrenceDue, "k2", ReminderUrgency.Soon, new DateOnly(2026, 11, 23), 2000m, "Aluguel"),
        ];

        var email = ReminderEmails.Digest("pessoa@exemplo.com", reminders, "http://app/");

        Assert.Equal("Expense Tracking: 2 avisos de vencimento", email.Subject);
        Assert.Contains("Fatura Nubank •••• 1234: venceu em 05/11, falta R$ 1.234,50", email.TextBody);
        Assert.Contains("Aluguel: vence em 23/11, R$ 2.000,00", email.TextBody);
        Assert.Contains("<li>Aluguel: vence em 23/11, R$ 2.000,00</li>", email.HtmlBody);
    }

    [Fact]
    public void Digest_DescribesThePendingAdjustment_WithTheIndex_OrWaitingForIt()
    {
        // RJT-9 (ADR-0010, decisao 10).
        Reminder[] reminders =
        [
            new(ReminderKind.RecurrenceAdjustment, "a1", ReminderUrgency.Soon, new DateOnly(2026, 11, 1), 2084m, "Reajuste de Aluguel",
                PreviousAmount: 2000m, Rate: 4.2m, Index: "IGPM"),
            new(ReminderKind.RecurrenceAdjustment, "a2", ReminderUrgency.Soon, new DateOnly(2026, 11, 1), 900m, "Reajuste de Escola",
                PreviousAmount: 900m, Index: "IPCA"),
            new(ReminderKind.RecurrenceAdjustment, "a3", ReminderUrgency.Soon, new DateOnly(2026, 11, 1), 525m, "Reajuste de Seguro",
                PreviousAmount: 500m, Rate: 5m),
        ];

        var email = ReminderEmails.Digest("pessoa@exemplo.com", reminders, "http://app/");

        Assert.Contains("Reajuste de Aluguel: R$ 2.000,00 → R$ 2.084,00 (IGP-M 4,2%)", email.TextBody);
        Assert.Contains("Reajuste de Escola: aguardando o IPCA; confirme na recorrência", email.TextBody);
        Assert.Contains("Reajuste de Seguro: R$ 500,00 → R$ 525,00 (5%)", email.TextBody);
    }
}
