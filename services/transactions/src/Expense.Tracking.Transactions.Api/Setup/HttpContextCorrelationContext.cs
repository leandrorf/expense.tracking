using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Api.Setup;

/// <summary>Le o correlation id do request atual (header posto pelo <see cref="ObservabilitySetup"/>).</summary>
public sealed class HttpContextCorrelationContext(IHttpContextAccessor httpContextAccessor) : ICorrelationContext
{
    public string? CorrelationId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers[ObservabilitySetup.CorrelationIdHeader].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
