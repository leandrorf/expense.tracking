using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <summary>
/// NTF-2/NTF-8: monta os avisos de um usuario a partir dos cartoes dele (lidos com o token do usuario na
/// API, ou pelo endpoint interno do accounts no job do e-mail), das recorrencias projetadas, das
/// preferencias e das dispensas. Um so calculo para a tela e para o e-mail.
/// </summary>
public sealed class ReminderComposer(IProjectionQueries projection, IReminderStore store, IBudgetRepository budgets)
{
    public async Task<IReadOnlyList<Reminder>> ComposeAsync(
        Guid ownerUserId, IReadOnlyList<CardInfo> cards, ReminderSettings settings, DateOnly today, CancellationToken cancellationToken)
    {
        var invoices = new List<ReminderInvoice>();
        if (settings.Invoices)
        {
            foreach (var card in cards)
            {
                var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
                invoices.AddRange(loaded.Invoices.Select(i => new ReminderInvoice(card.Id, card.Label, card.LastFourDigits, i)));
            }
        }

        var recurrences = settings.Recurrences
            ? (await projection.GetRecurrencesAsync(ownerUserId, cancellationToken))
                .Select(r => new ReminderRecurrence(r.Id, r.Description, r.TransactionType != "Income", r.Amount, r.Frequency,
                    r.StartsOn, r.EndsOn, r.ClosedPeriods, r.Amounts))
                .ToList()
            : [];

        // NTF-9: situacao dos orcamentos no mes corrente (mesma regra da aba Orcamentos, BDG-4).
        IReadOnlyList<BudgetMonthStatus> budgetStatus = [];
        if (settings.Budgets)
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            var list = await budgets.ListAsync(ownerUserId, cancellationToken);
            if (list.Count > 0)
            {
                var monthTransactions = await projection.GetTransactionsAsync(
                    ownerUserId, new ReportFilter(From: first, To: first.AddMonths(1).AddDays(-1)), cancellationToken);
                budgetStatus = BudgetStatusCalculator.Calculate(first, list, monthTransactions);
            }
        }

        // NTF-5: o dispensado some ate a chave dele mudar (ADR-0009, decisao 5).
        var dismissed = await store.ListDismissedKeysAsync(ownerUserId, cancellationToken);
        return ReminderCalculator.Calculate(
                today, settings.LeadDays, invoices, recurrences, settings.Invoices, settings.Recurrences, budgetStatus)
            .Where(r => !dismissed.Contains(r.Key))
            .ToList();
    }
}

/// <summary>NTF-8 (ADR-0009, decisao 6): cartoes de um dono lidos sem token de usuario (endpoint interno do accounts).</summary>
public interface IServiceCardDirectory
{
    Task<IReadOnlyList<CardInfo>> ListForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken);
}

/// <summary>Um e-mail a enviar: texto simples e HTML com o mesmo conteudo.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>NTF-8: envio de e-mail (SMTP). Sem servidor configurado, a implementacao so registra no log.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>NTF-8: endereco publico da SPA, para os links dos e-mails (confirmacao e resumo).</summary>
public sealed record AppLinks(Uri PublicAppUrl)
{
    public string ConfirmEmail(string token) => new Uri(PublicAppUrl, $"confirm-email?token={Uri.EscapeDataString(token)}").ToString();

    public string Home => PublicAppUrl.ToString();
}
