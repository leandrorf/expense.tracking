using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Application.Reports;

/// <summary>Monta o resumo consolidado buscando lancamentos + escopos e delegando o calculo.</summary>
public sealed class GetSummaryHandler(ITransactionsClient transactionsClient, IAccountsClient accountsClient)
{
    public async Task<ReportSummary> HandleAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var transactionsTask = transactionsClient.GetTransactionsAsync(filter, cancellationToken);
        var scopesTask = accountsClient.GetAccountScopesAsync(cancellationToken);
        await Task.WhenAll(transactionsTask, scopesTask);

        return SummaryCalculator.Calculate(transactionsTask.Result, scopesTask.Result);
    }
}
