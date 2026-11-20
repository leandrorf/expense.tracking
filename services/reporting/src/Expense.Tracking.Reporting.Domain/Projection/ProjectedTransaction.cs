namespace Expense.Tracking.Reporting.Domain.Projection;

/// <summary>
/// EVT-2: um lancamento como o reporting o conhece, montado a partir dos eventos do transactions
/// (ADR-0001). Uma linha por lancamento: o resumo filtra por dono, conta e periodo.
/// </summary>
public sealed class ProjectedTransaction
{
    public Guid TransactionId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>
    /// Escopo da conta (<c>Personal</c>/<c>Business</c>). Nulo para lancamentos anteriores ao
    /// outbox do transactions, cujo escopo e desconhecido.
    /// </summary>
    public string? AccountScope { get; set; }

    /// <summary><c>Expense</c> ou <c>Income</c>.</summary>
    public string TransactionType { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateOnly OccurredOn { get; set; }

    /// <summary>Sequencia do ultimo evento do feed aplicado a esta linha.</summary>
    public long LastEventSequence { get; set; }
}
