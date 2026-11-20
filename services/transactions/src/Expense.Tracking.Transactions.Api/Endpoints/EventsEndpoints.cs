using System.Text.Json;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Events;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Api.Endpoints;

/// <summary>Um evento do feed. <c>payload</c> segue o contrato do <c>eventType</c>/<c>version</c>.</summary>
public sealed record FeedEventResponse(
    long Sequence,
    Guid EventId,
    string EventType,
    int Version,
    Guid AggregateId,
    Guid OwnerUserId,
    DateTimeOffset OccurredAtUtc,
    string? TraceParent,
    JsonElement Payload);

/// <summary>Pagina do feed: ler a proxima com <c>after = lastSequence</c>.</summary>
public sealed record EventFeedResponse(IReadOnlyList<FeedEventResponse> Events, long LastSequence);

/// <summary>
/// EVT-1 (ADR-0001): feed de eventos do transactions, lido por polling pelo reporting.
/// Interno: so aceita token de servico com o escopo <c>events.read</c> e nao passa pelo gateway.
/// </summary>
public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/events", async (ReadEventFeedHandler handler, CancellationToken ct, long? after, int? limit) =>
        {
            var cursor = after ?? 0;
            if (cursor < 0)
            {
                return HttpResults.Problem(Error.Validation("events.after_invalid", "'after' deve ser zero ou positivo."));
            }

            var pageSize = limit ?? ReadEventFeedHandler.DefaultLimit;
            if (pageSize is < 1 or > ReadEventFeedHandler.MaxLimit)
            {
                return HttpResults.Problem(Error.Validation(
                    "events.limit_invalid", $"'limit' deve estar entre 1 e {ReadEventFeedHandler.MaxLimit}."));
            }

            var page = await handler.HandleAsync(cursor, pageSize, ct);
            var events = page.Events
                .Select(e => new FeedEventResponse(
                    e.Sequence, e.EventId, e.EventType, e.Version, e.AggregateId, e.OwnerUserId,
                    e.OccurredAtUtc, e.TraceParent, JsonSerializer.Deserialize<JsonElement>(e.Payload)))
                .ToArray();

            return Results.Ok(new EventFeedResponse(events, page.LastSequence));
        })
        .WithTags("Events")
        .RequireAuthorization(AuthenticationSetup.EventsReadPolicy);

        return app;
    }
}
