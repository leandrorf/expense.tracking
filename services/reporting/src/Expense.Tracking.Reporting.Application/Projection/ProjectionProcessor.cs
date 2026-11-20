using System.Diagnostics;
using System.Text.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Projection;

namespace Expense.Tracking.Reporting.Application.Projection;

/// <summary>Resultado de um lote: eventos aplicados, pulados (tipo desconhecido) e o novo checkpoint.</summary>
public sealed record ProjectionBatchResult(int Applied, int Skipped, long Checkpoint)
{
    public int Read => Applied + Skipped;
}

/// <summary>Configuracao do consumo do feed.</summary>
public sealed record ProjectionOptions(int BatchSize, TimeSpan PollInterval, bool Enabled)
{
    public static readonly ProjectionOptions Default = new(100, TimeSpan.FromSeconds(2), Enabled: true);
}

/// <summary>Telemetria da projecao: um span por lote, ligado aos traces de origem dos eventos.</summary>
public static class ProjectionTelemetry
{
    public const string SourceName = "Expense.Tracking.Reporting.Projection";

    public static readonly ActivitySource Source = new(SourceName);
}

/// <summary>
/// EVT-2 (ADR-0001): aplica um lote do feed na projecao. Le a partir do checkpoint, aplica os
/// eventos e grava projecao e checkpoint no <b>mesmo commit</b>: ou o lote inteiro entra, ou nada
/// entra e o mesmo lote e relido. As regras de aplicacao sao idempotentes (cadastro = upsert,
/// exclusao = remover se existir), entao reler eventos ja aplicados — inclusive com o checkpoint
/// zerado, para reconstruir a projecao — chega ao mesmo estado, sem duplicar valores.
/// </summary>
public sealed class ProjectionProcessor(IEventFeedClient feed, IProjectionStore store, ProjectionOptions options)
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task<ProjectionBatchResult> ProcessNextBatchAsync(CancellationToken cancellationToken)
    {
        var checkpoint = await store.GetCheckpointAsync(cancellationToken);
        var page = await feed.ReadAsync(checkpoint, options.BatchSize, cancellationToken);
        if (page.Events.Count == 0)
        {
            return new ProjectionBatchResult(0, 0, checkpoint);
        }

        using var activity = StartBatchActivity(page, checkpoint);

        int applied = 0, skipped = 0;
        foreach (var feedEvent in page.Events)
        {
            if (await ApplyAsync(feedEvent, cancellationToken))
            {
                applied++;
            }
            else
            {
                skipped++;
            }
        }

        store.SetCheckpoint(page.LastSequence);
        await store.SaveChangesAsync(cancellationToken);

        activity?.SetTag("projection.events.applied", applied);
        activity?.SetTag("projection.events.skipped", skipped);
        return new ProjectionBatchResult(applied, skipped, page.LastSequence);
    }

    /// <summary>Aplica um evento. Retorna false quando o tipo e desconhecido (pulado).</summary>
    private async Task<bool> ApplyAsync(FeedEvent feedEvent, CancellationToken cancellationToken)
    {
        switch (feedEvent.EventType)
        {
            case FeedEventTypes.TransactionRegistered:
                EnsureSupported(feedEvent);
                await ApplyRegisteredAsync(feedEvent, Read<TransactionRegisteredV1>(feedEvent), cancellationToken);
                return true;

            case FeedEventTypes.TransactionDeleted:
                EnsureSupported(feedEvent);
                await ApplyDeletedAsync(Read<TransactionDeletedV1>(feedEvent), cancellationToken);
                return true;

            default:
                // Tipo novo que o reporting ainda nao usa: nao afeta a projecao.
                return false;
        }
    }

    private async Task ApplyRegisteredAsync(FeedEvent feedEvent, TransactionRegisteredV1 e, CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(e.TransactionId, cancellationToken);
        if (row is null)
        {
            row = new ProjectedTransaction { TransactionId = e.TransactionId };
            store.Add(row);
        }

        row.OwnerUserId = e.OwnerUserId;
        row.AccountId = e.AccountId;
        row.AccountScope = e.AccountScope;
        row.TransactionType = e.TransactionType;
        row.PaymentMethod = e.PaymentMethod;
        row.Category = e.Category;
        row.Amount = e.Amount;
        row.Currency = e.Currency;
        row.OccurredOn = e.OccurredOn;
        row.LastEventSequence = feedEvent.Sequence;
    }

    private async Task ApplyDeletedAsync(TransactionDeletedV1 e, CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(e.TransactionId, cancellationToken);
        if (row is not null)
        {
            store.Remove(row);
        }
    }

    private static void EnsureSupported(FeedEvent feedEvent)
    {
        if (feedEvent.Version != FeedEventTypes.SupportedVersion)
        {
            throw new UnsupportedEventVersionException(feedEvent);
        }
    }

    private static T Read<T>(FeedEvent feedEvent) =>
        feedEvent.Payload.Deserialize<T>(PayloadJson)
        ?? throw new InvalidOperationException($"Payload vazio no evento {feedEvent.EventType} (sequencia {feedEvent.Sequence}).");

    private static Activity? StartBatchActivity(FeedPage page, long checkpoint)
    {
        // Liga o processamento aos traces das requests que gravaram cada evento.
        var links = page.Events
            .Select(e => ActivityContext.TryParse(e.TraceParent, null, out var context) ? context : default)
            .Where(context => context != default)
            .Select(context => new ActivityLink(context))
            .ToArray();

        var activity = ProjectionTelemetry.Source.StartActivity(
            "projection apply-batch", ActivityKind.Consumer, parentContext: default, links: links);
        activity?.SetTag("projection.checkpoint.from", checkpoint);
        activity?.SetTag("projection.checkpoint.to", page.LastSequence);
        activity?.SetTag("projection.events.read", page.Events.Count);
        return activity;
    }
}
