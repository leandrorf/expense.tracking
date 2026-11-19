using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.Application.Abstractions;

public sealed record ReportFilter(Guid? AccountId = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>Le os lancamentos do servico transactions (escopados pelo token do usuario).</summary>
public interface ITransactionsClient
{
    Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(ReportFilter filter, CancellationToken cancellationToken);
}

/// <summary>Le as contas do servico accounts para mapear conta -> escopo (PF/PJ).</summary>
public interface IAccountsClient
{
    Task<IReadOnlyDictionary<Guid, string>> GetAccountScopesAsync(CancellationToken cancellationToken);
}

/// <summary>Fornece o access token do usuario atual para encaminhar entre servicos.</summary>
public interface IUserTokenAccessor
{
    string? GetAccessToken();
}

/// <summary>Fornece o correlation id da request atual para propagar entre servicos (OBS-1).</summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
