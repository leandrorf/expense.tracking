using Expense.Tracking.Transactions.Application.Abstractions;

namespace Expense.Tracking.Transactions.Application.Events;

/// <summary>Configuracao do feed de eventos.</summary>
/// <param name="SafetyMargin">
/// So eventos gravados ha mais que este tempo sao servidos. Cobre a janela em que uma transacao
/// de banco ja pegou sua sequencia mas ainda nao commitou (ver <see cref="ReadEventFeedHandler"/>).
/// </param>
public sealed record EventFeedOptions(TimeSpan SafetyMargin)
{
    public static readonly TimeSpan DefaultSafetyMargin = TimeSpan.FromSeconds(5);
}

/// <summary>Uma pagina do feed. <see cref="LastSequence"/> e o cursor para a proxima leitura.</summary>
public sealed record EventFeedPage(IReadOnlyList<FeedEvent> Events, long LastSequence);

/// <summary>
/// EVT-1: le o feed a partir de um cursor. A sequencia vem de auto-incremento, entao uma transacao
/// que pegou a sequencia 10 pode commitar depois da 11; um leitor que avancasse para 11 perderia o
/// 10. Para evitar isso o feed devolve so o <b>prefixo contiguo</b> de eventos mais antigos que a
/// margem de seguranca: ao encontrar o primeiro evento recente demais, para ali, mesmo que haja
/// eventos mais antigos depois dele.
/// </summary>
public sealed class ReadEventFeedHandler(IEventFeed feed, EventFeedOptions options, TimeProvider timeProvider)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    public async Task<EventFeedPage> HandleAsync(long after, int limit, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - options.SafetyMargin;
        var candidates = await feed.ReadAfterAsync(after, limit, cancellationToken);

        var visible = candidates.TakeWhile(e => e.OccurredAtUtc <= cutoff).ToArray();
        return new EventFeedPage(visible, visible.Length > 0 ? visible[^1].Sequence : after);
    }
}
