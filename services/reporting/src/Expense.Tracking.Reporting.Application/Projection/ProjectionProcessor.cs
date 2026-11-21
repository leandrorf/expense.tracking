using System.Diagnostics;
using System.Text.Json;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Domain.Projection;

namespace Expense.Tracking.Reporting.Application.Projection;

/// <summary>
/// Resultado de um lote: eventos aplicados, pulados (tipo desconhecido), o novo checkpoint e a
/// maior sequencia gravada no feed.
/// </summary>
public sealed record ProjectionBatchResult(int Applied, int Skipped, long Checkpoint, long HeadSequence)
{
    public int Read => Applied + Skipped;

    /// <summary>Eventos gravados no outbox e ainda nao aplicados (inclusive os que estao na margem do feed).</summary>
    public long Lag => Math.Max(0, HeadSequence - Checkpoint);
}

/// <summary>Configuracao do consumo do feed.</summary>
public sealed record ProjectionOptions(int BatchSize, TimeSpan PollInterval, bool Enabled)
{
    public static readonly ProjectionOptions Default = new(100, TimeSpan.FromSeconds(2), Enabled: true);
}

/// <summary>
/// Telemetria da projecao: um span por lote, ligado aos traces de origem dos eventos. O meter das
/// metricas de atraso (<see cref="ProjectionStatus"/>) usa o mesmo nome.
/// </summary>
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
        // Sem headSequence (transactions antigo, 0), o head cai para o cursor: atraso 0, nunca negativo.
        var head = Math.Max(page.HeadSequence, page.LastSequence);
        if (page.Events.Count == 0)
        {
            return new ProjectionBatchResult(0, 0, checkpoint, Math.Max(head, checkpoint));
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
        return new ProjectionBatchResult(applied, skipped, page.LastSequence, head);
    }

    /// <summary>Aplica um evento. Retorna false quando o tipo e desconhecido (pulado).</summary>
    private async Task<bool> ApplyAsync(FeedEvent feedEvent, CancellationToken cancellationToken)
    {
        switch (feedEvent.EventType)
        {
            case FeedEventTypes.TransactionRegistered:
                EnsureSupported(feedEvent);
                await ApplyRegisteredAsync(feedEvent, ReadRegistered(feedEvent), cancellationToken);
                return true;

            case FeedEventTypes.TransactionDeleted:
                EnsureSupported(feedEvent);
                await ApplyDeletedAsync(Read<TransactionDeletedV1>(feedEvent), cancellationToken);
                return true;

            case FeedEventTypes.TransactionAccountScopeResolved:
                EnsureSupported(feedEvent);
                await ApplyScopeResolvedAsync(feedEvent, Read<TransactionAccountScopeResolvedV1>(feedEvent), cancellationToken);
                return true;

            case FeedEventTypes.TransactionCardDetailsResolved:
                EnsureSupported(feedEvent);
                await ApplyCardDetailsResolvedAsync(feedEvent, Read<TransactionCardDetailsResolvedV1>(feedEvent), cancellationToken);
                return true;

            case FeedEventTypes.CardInvoicePaid:
                EnsureSupported(feedEvent);
                await ApplyCardInvoicePaidAsync(feedEvent, Read<CardInvoicePaidV1>(feedEvent), cancellationToken);
                return true;

            case FeedEventTypes.CardInvoicePaymentDeleted:
                EnsureSupported(feedEvent);
                await ApplyCardInvoicePaymentDeletedAsync(Read<CardInvoicePaymentDeletedV1>(feedEvent), cancellationToken);
                return true;

            default:
                // Tipo novo que o reporting ainda nao usa: nao afeta a projecao.
                return false;
        }
    }

    /// <summary>v1 e lida como v2 a vista, sem cartao nem descricao (o backfill completa as compras no cartao).</summary>
    private static TransactionRegisteredV2 ReadRegistered(FeedEvent feedEvent)
    {
        if (feedEvent.Version >= 2)
        {
            return Read<TransactionRegisteredV2>(feedEvent);
        }

        var v1 = Read<TransactionRegisteredV1>(feedEvent);
        return new TransactionRegisteredV2(
            v1.TransactionId, v1.OwnerUserId, v1.AccountId, v1.AccountScope, v1.TransactionType, v1.PaymentMethod,
            v1.Category, v1.Amount, v1.Currency, v1.OccurredOn, Description: null, CreditCardId: null, Installments: 1);
    }

    private async Task ApplyRegisteredAsync(FeedEvent feedEvent, TransactionRegisteredV2 e, CancellationToken cancellationToken)
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
        row.Description = e.Description;
        row.CreditCardId = e.CreditCardId;
        row.Installments = e.Installments;
        row.LastEventSequence = feedEvent.Sequence;
    }

    private async Task ApplyCardDetailsResolvedAsync(
        FeedEvent feedEvent, TransactionCardDetailsResolvedV1 e, CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(e.TransactionId, cancellationToken);
        if (row is not null)
        {
            row.Description = e.Description;
            row.CreditCardId = e.CreditCardId;
            row.Installments = e.Installments;
            row.LastEventSequence = feedEvent.Sequence;
        }
    }

    private async Task ApplyDeletedAsync(TransactionDeletedV1 e, CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(e.TransactionId, cancellationToken);
        if (row is not null)
        {
            store.Remove(row);
        }
    }

    // INV-6: upsert pelo id do pagamento, como o cadastro de lancamento (reler o evento nao duplica).
    private async Task ApplyCardInvoicePaidAsync(FeedEvent feedEvent, CardInvoicePaidV1 e, CancellationToken cancellationToken)
    {
        var dueMonth = DateOnly.ParseExact(e.InvoiceDueMonth, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var row = await store.FindPaymentAsync(e.PaymentId, cancellationToken);
        if (row is null)
        {
            row = new ProjectedCardPayment { PaymentId = e.PaymentId };
            store.AddPayment(row);
        }

        row.OwnerUserId = e.OwnerUserId;
        row.CreditCardId = e.CreditCardId;
        row.InvoiceDueMonth = dueMonth;
        row.AccountId = e.AccountId;
        row.Amount = e.Amount;
        row.Currency = e.Currency;
        row.PaidOn = e.PaidOn;
        row.LastEventSequence = feedEvent.Sequence;
    }

    private async Task ApplyCardInvoicePaymentDeletedAsync(CardInvoicePaymentDeletedV1 e, CancellationToken cancellationToken)
    {
        var row = await store.FindPaymentAsync(e.PaymentId, cancellationToken);
        if (row is not null)
        {
            store.RemovePayment(row);
        }
    }

    // EVT-4: so completa uma linha existente. Se o lancamento ja foi excluido, nao o recria.
    private async Task ApplyScopeResolvedAsync(FeedEvent feedEvent, TransactionAccountScopeResolvedV1 e, CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(e.TransactionId, cancellationToken);
        if (row is not null)
        {
            row.AccountScope = e.AccountScope;
            row.LastEventSequence = feedEvent.Sequence;
        }
    }

    private static void EnsureSupported(FeedEvent feedEvent)
    {
        if (feedEvent.Version < 1 || feedEvent.Version > FeedEventTypes.SupportedVersionOf(feedEvent.EventType))
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
