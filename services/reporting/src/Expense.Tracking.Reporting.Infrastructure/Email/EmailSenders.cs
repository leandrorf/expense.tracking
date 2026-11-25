using Expense.Tracking.Reporting.Application.Reminders;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Expense.Tracking.Reporting.Infrastructure.Email;

/// <summary>NTF-8: servidor SMTP (<c>Smtp:*</c>). Sem <see cref="Host"/>, nada e enviado.</summary>
public sealed record SmtpOptions(string? Host, int Port, string From, string? User, string? Password, bool UseTls);

/// <summary>NTF-8 (ADR-0009, decisao 6): envio por SMTP com MailKit. Uma conexao por e-mail (o volume e baixo).</summary>
internal sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host!, options.Port,
            options.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(options.User))
        {
            await client.AuthenticateAsync(options.User, options.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>
/// Sem <c>Smtp:Host</c>: registra que um e-mail deixaria de sair, sem o endereco nem o conteudo (dados
/// pessoais e financeiros nao vao para o log).
/// </summary>
internal sealed class DisabledEmailSender(ILogger<DisabledEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning("Smtp:Host nao configurado: e-mail \"{Subject}\" nao enviado.", message.Subject);
        return Task.CompletedTask;
    }
}
