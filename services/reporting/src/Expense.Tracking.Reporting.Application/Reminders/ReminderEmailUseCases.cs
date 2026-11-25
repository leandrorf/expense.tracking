using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Reminders;
using Microsoft.Extensions.Logging;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <summary>NTF-8: o que o usuario informa sobre o resumo por e-mail.</summary>
public sealed record ReminderEmailInput(string? Email, bool Enabled, int SendHour);

/// <param name="EmailConfirmed">O endereco atual foi confirmado pelo link (so assim o resumo sai).</param>
public sealed record ReminderEmailDto(string? Email, bool Enabled, bool EmailConfirmed, int SendHour)
{
    public static ReminderEmailDto From(ReminderSettings s) => new(s.Email, s.EmailEnabled, s.EmailConfirmed, s.SendHour);
}

/// <summary>Token de confirmacao: aleatorio, de uso unico, guardado so como hash.</summary>
public static class EmailTokens
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();
}

/// <summary>
/// NTF-8 (ADR-0009, decisao 6): grava endereco, liga/desliga e hora do resumo. Trocar o endereco zera a
/// confirmacao e manda um e-mail com o link (token de uso unico, 24 h) para o endereco novo.
/// </summary>
public sealed class UpdateReminderEmailHandler(
    IReminderStore store, IEmailSender sender, AppLinks links, TimeProvider timeProvider)
{
    public async Task<Result<ReminderEmailDto>> HandleAsync(Guid ownerUserId, ReminderEmailInput input, CancellationToken cancellationToken)
    {
        var settings = await store.FindSettingsAsync(ownerUserId, cancellationToken);
        var isNew = settings is null;
        settings ??= ReminderSettings.Default(ownerUserId);

        var token = EmailTokens.NewToken();
        var changed = settings.UpdateEmail(input.Email, input.Enabled, input.SendHour, EmailTokens.HashOf(token), timeProvider.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result.Failure<ReminderEmailDto>(changed.Error);
        }

        if (isNew)
        {
            store.AddSettings(settings);
        }

        await store.SaveChangesAsync(cancellationToken);
        if (changed.Value)
        {
            await sender.SendAsync(ReminderEmails.Confirmation(settings.Email!, links.ConfirmEmail(token)), cancellationToken);
        }

        return ReminderEmailDto.From(settings);
    }
}

/// <summary>NTF-8: manda de novo o link de confirmacao do endereco atual (com token novo).</summary>
public sealed class ResendReminderEmailConfirmationHandler(
    IReminderStore store, IEmailSender sender, AppLinks links, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var settings = await store.FindSettingsAsync(ownerUserId, cancellationToken);
        var token = EmailTokens.NewToken();
        var resent = settings?.ResendConfirmation(EmailTokens.HashOf(token), timeProvider.GetUtcNow())
            ?? Result.Failure(Error.Validation("reminders.email_not_pending", "Nao ha e-mail esperando confirmacao."));
        if (resent.IsFailure)
        {
            return resent;
        }

        await store.SaveChangesAsync(cancellationToken);
        await sender.SendAsync(ReminderEmails.Confirmation(settings!.Email!, links.ConfirmEmail(token)), cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// NTF-8: confirma o endereco com o token do link. A SPA, ja logada, chama esta rota: o token so vale
/// para o usuario que pediu (nada de rota anonima).
/// </summary>
public sealed class ConfirmReminderEmailHandler(IReminderStore store, TimeProvider timeProvider)
{
    public async Task<Result<ReminderEmailDto>> HandleAsync(Guid ownerUserId, string? token, CancellationToken cancellationToken)
    {
        var settings = await store.FindSettingsAsync(ownerUserId, cancellationToken);
        var confirmed = settings is null || string.IsNullOrWhiteSpace(token)
            ? Result.Failure(Error.Validation("reminders.email_token_invalid", "Link de confirmacao invalido ou vencido: peca um novo nas preferencias."))
            : settings.ConfirmEmail(EmailTokens.HashOf(token), timeProvider.GetUtcNow());
        if (confirmed.IsFailure)
        {
            return Result.Failure<ReminderEmailDto>(confirmed.Error);
        }

        await store.SaveChangesAsync(cancellationToken);
        return ReminderEmailDto.From(settings!);
    }
}

/// <summary>Conteudo dos e-mails: so descricao, valor e data, e links que levam ao app (que pede login).</summary>
public static class ReminderEmails
{
    // O servico roda em modo de globalizacao invariante (sem a cultura pt-BR): o formato brasileiro e montado aqui.
    private static readonly NumberFormatInfo Brl = new()
    {
        CurrencySymbol = "R$",
        CurrencyDecimalSeparator = ",",
        CurrencyGroupSeparator = ".",
        CurrencyDecimalDigits = 2,
        CurrencyPositivePattern = 2, // "R$ 1.234,56"
        CurrencyNegativePattern = 12, // "R$ -1.234,56"
    };

    public static EmailMessage Confirmation(string to, string link) => new(
        to,
        "Confirme o e-mail dos avisos do Expense Tracking",
        $"Para receber o resumo diário de vencimentos neste endereço, abra o link (vale por 24 horas):\n{link}\n\nSe não foi você, ignore este e-mail.",
        $"<p>Para receber o resumo diário de vencimentos neste endereço, <a href=\"{WebUtility.HtmlEncode(link)}\">confirme o e-mail</a> (o link vale por 24 horas).</p><p>Se não foi você, ignore este e-mail.</p>");

    public static EmailMessage Digest(string to, IReadOnlyList<Reminder> reminders, string appLink)
    {
        var lines = reminders.Select(LineOf).ToList();
        var subject = reminders.Count == 1 ? "1 aviso de vencimento" : $"{reminders.Count} avisos de vencimento";
        var text = $"{subject}:\n\n{string.Join('\n', lines.Select(l => "- " + l))}\n\nAbra o app: {appLink}\n"
            + "Para não receber mais, desligue o resumo nas preferências dos avisos.";
        var html = $"<p>{WebUtility.HtmlEncode(subject)}:</p><ul>{string.Concat(lines.Select(l => $"<li>{WebUtility.HtmlEncode(l)}</li>"))}</ul>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(appLink)}\">Abrir o app</a></p>"
            + "<p>Para não receber mais, desligue o resumo nas preferências dos avisos.</p>";
        return new EmailMessage(to, $"Expense Tracking: {subject}", text, html);
    }

    private static string LineOf(Reminder r)
    {
        var date = r.DueOn.ToString("dd/MM", CultureInfo.InvariantCulture);
        var money = r.Amount.ToString("C", Brl);
        return r.Kind switch
        {
            ReminderKind.InvoiceOverdue => $"{r.Title}: venceu em {date}, falta {money}",
            ReminderKind.InvoiceDue => $"{r.Title}: vence em {date}, falta {money}",
            ReminderKind.RecurrenceDue => $"{r.Title}: vence em {date}, {money}",
            ReminderKind.RecurrencesToLaunch => $"{r.Title} (pendentes desde {date})",
            ReminderKind.Budget => $"{r.Title}: {money} gastos de {r.Limit.ToString("C", Brl)} no mês",
            _ => $"{r.Title}: {date}",
        };
    }
}

/// <summary>
/// NTF-8 (ADR-0009, decisao 6): uma passada do resumo diario. Para cada usuario com resumo ligado e
/// endereco confirmado, na hora escolhida (fuso do app) e sem envio no dia, calcula os avisos (com os
/// cartoes lidos pelo endpoint interno do accounts) e envia so se houver aviso que nao estava no ultimo
/// resumo. Falha de um usuario nao para os outros.
/// </summary>
public sealed class SendReminderDigestsJob(
    IReminderStore store,
    IServiceCardDirectory cards,
    ReminderComposer composer,
    IEmailSender sender,
    AppLinks links,
    InvoiceOptions options,
    TimeProvider timeProvider,
    ILogger<SendReminderDigestsJob> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, options.TimeZone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var sent = 0;

        foreach (var settings in await store.ListDigestCandidatesAsync(cancellationToken))
        {
            if (!settings.IsDigestDue(today, local.Hour))
            {
                continue;
            }

            try
            {
                var ownerCards = settings.Invoices ? await cards.ListForOwnerAsync(settings.OwnerUserId, cancellationToken) : [];
                var reminders = await composer.ComposeAsync(settings.OwnerUserId, ownerCards, settings, today, cancellationToken);
                var keys = reminders.Select(r => r.Key).ToList();
                if (!settings.HasNewKeys(keys))
                {
                    continue;
                }

                await sender.SendAsync(ReminderEmails.Digest(settings.Email!, reminders, links.Home), cancellationToken);
                settings.RecordDigestSent(today, keys, now);
                await store.SaveChangesAsync(cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // O endereco nao vai para o log (dado pessoal): so o dono.
                logger.LogWarning(ex, "Resumo de avisos do usuario {OwnerUserId} nao enviado; tenta na proxima passada.", settings.OwnerUserId);
            }
        }

        return sent;
    }
}
