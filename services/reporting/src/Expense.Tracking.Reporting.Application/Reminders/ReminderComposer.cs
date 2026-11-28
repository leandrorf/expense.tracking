using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Budgets;
using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Application.Goals;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Goals;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <summary>
/// NTF-2/NTF-8: monta os avisos de um usuario a partir dos cartoes dele (lidos com o token do usuario na
/// API, ou pelo endpoint interno do accounts no job do e-mail), das recorrencias projetadas, das
/// preferencias e das dispensas. Um so calculo para a tela e para o e-mail.
/// </summary>
/// <remarks>
/// MET-9 (ADR-0013, regra 10): com a preferencia de orcamentos ligada, tambem a meta do mes atual fora e o objetivo
/// atrasado perto do prazo, pelos mesmos calculos da aba Metas.
/// </remarks>
public sealed class ReminderComposer(
    IProjectionQueries projection,
    IReminderStore store,
    IBudgetRepository budgets,
    IMonthlyGoalRepository monthlyGoals,
    ISavingsObjectiveRepository objectives,
    GetCashForecastHandler forecast,
    InvoiceOptions options)
{
    public async Task<IReadOnlyList<Reminder>> ComposeAsync(
        Guid ownerUserId, IReadOnlyList<ScopedCard> cards, ReminderSettings settings, DateOnly today, CancellationToken cancellationToken)
    {
        var invoices = new List<ReminderInvoice>();
        if (settings.Invoices)
        {
            foreach (var (card, _) in cards)
            {
                var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
                invoices.AddRange(loaded.Invoices.Select(i => new ReminderInvoice(card.Id, card.Label, card.LastFourDigits, i)));
            }
        }

        var recurrences = settings.Recurrences
            ? (await projection.GetRecurrencesAsync(ownerUserId, cancellationToken))
                .Select(r => new ReminderRecurrence(r.Id, r.Description, r.TransactionType != "Income", r.Amount, r.Frequency,
                    r.StartsOn, r.EndsOn, r.ClosedPeriods, r.Amounts, r.Adjustment))
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

        IReadOnlyList<MonthlyGoalCheck> goalChecks = [];
        IReadOnlyList<SavingsObjectiveCheck> objectiveChecks = [];
        if (settings.Budgets)
        {
            goalChecks = await MonthlyGoalChecksAsync(ownerUserId, cards, today, cancellationToken);
            objectiveChecks = await ObjectiveChecksAsync(ownerUserId, today, cancellationToken);
        }

        // NTF-5: o dispensado some ate a chave dele mudar (ADR-0009, decisao 5).
        var dismissed = await store.ListDismissedKeysAsync(ownerUserId, cancellationToken);
        return ReminderCalculator.Calculate(
                today, settings.LeadDays, invoices, recurrences, settings.Invoices, settings.Recurrences, budgetStatus,
                goalChecks, objectiveChecks)
            .Where(r => !dismissed.Contains(r.Key))
            .ToList();
    }

    /// <summary>MET-9: a meta do mes atual de PF e PJ, avaliada sobre o fluxo da previsao.</summary>
    private async Task<IReadOnlyList<MonthlyGoalCheck>> MonthlyGoalChecksAsync(
        Guid ownerUserId, IReadOnlyList<ScopedCard> cards, DateOnly today, CancellationToken cancellationToken)
    {
        var goals = await monthlyGoals.ListAsync(ownerUserId, cancellationToken);
        var month = new DateOnly(today.Year, today.Month, 1);
        var terms = new List<(GoalScope Scope, MonthlyGoalTerm Term)>();
        foreach (var scope in new[] { GoalScope.Personal, GoalScope.Business })
        {
            if (goals.Where(g => g.Scope == scope).Select(g => g.TermFor(month)).FirstOrDefault(t => t is not null) is { } term)
            {
                terms.Add((scope, term));
            }
        }

        if (terms.Count == 0)
        {
            return [];
        }

        var flows = (await forecast.FlowsAsync(ownerUserId, cards, today, 1, cancellationToken))[0];
        var checks = new List<MonthlyGoalCheck>();
        foreach (var (scope, term) in terms)
        {
            var flow = scope == GoalScope.Business ? flows.Business : flows.Personal;
            var figures = new MonthFigures(flow.Income, flow.Expense, flow.Invoices);
            var evaluation = MonthlyGoalStatusCalculator.Evaluate(term, figures);
            checks.Add(new MonthlyGoalCheck(scope, term.Kind, evaluation.Situation, evaluation.Target, figures));
        }

        return checks;
    }

    /// <summary>MET-9: os objetivos ativos com o ritmo de hoje.</summary>
    private async Task<IReadOnlyList<SavingsObjectiveCheck>> ObjectiveChecksAsync(
        Guid ownerUserId, DateOnly today, CancellationToken cancellationToken) =>
        (await objectives.ListAsync(ownerUserId, cancellationToken))
            .Where(o => !o.IsArchived)
            .Select(o => new SavingsObjectiveCheck(o.Id, o.Name, o.Scope, o.Deadline, SavingsPaceCalculator.Evaluate(
                o, today, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(o.CreatedAt, options.TimeZone).DateTime))))
            .ToList();
}

/// <summary>NTF-8 (ADR-0009, decisao 6): cartoes de um dono lidos sem token de usuario (endpoint interno do accounts).</summary>
public interface IServiceCardDirectory
{
    Task<IReadOnlyList<ScopedCard>> ListForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken);
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
