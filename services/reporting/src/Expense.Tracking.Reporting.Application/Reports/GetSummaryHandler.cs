using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Application.Reports;

/// <summary>
/// Monta o resumo consolidado do usuario a partir da projecao (EVT-3) e delega o calculo. A
/// projecao pode estar defasada em relacao ao transactions pelo atraso do worker.
/// </summary>
public sealed class GetSummaryHandler(IProjectionQueries projection)
{
    public async Task<ReportSummary> HandleAsync(Guid ownerUserId, ReportFilter filter, CancellationToken cancellationToken)
    {
        var records = await projection.GetTransactionsAsync(ownerUserId, filter, cancellationToken);
        return SummaryCalculator.Calculate(records);
    }
}
