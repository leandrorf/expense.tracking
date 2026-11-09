using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Infrastructure.Clients;

/// <summary>
/// OBS-1: propaga o correlation id da request atual nas chamadas ao servico accounts,
/// mantendo os logs de toda a cadeia (gateway -> transactions -> accounts) amarrados.
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
