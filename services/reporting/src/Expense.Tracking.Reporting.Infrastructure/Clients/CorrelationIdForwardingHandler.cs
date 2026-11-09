using Expense.Tracking.Reporting.Application.Abstractions;

namespace Expense.Tracking.Reporting.Infrastructure.Clients;

/// <summary>
/// OBS-1: propaga o correlation id da request atual nas chamadas aos servicos transactions
/// e accounts, mantendo os logs de toda a cadeia amarrados ao mesmo id.
/// </summary>
internal sealed class CorrelationIdForwardingHandler(ICorrelationContext correlationContext) : DelegatingHandler
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = correlationContext.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains(CorrelationIdHeader))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
