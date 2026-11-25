namespace Expense.Tracking.Reporting.Domain.Projection;

/// <summary>
/// REC-4 (ADR-0008, decisao 2): regra de recorrencia projetada a partir do <c>RecurrenceSaved</c>.
/// So alimenta a previsao: nunca entra no Resumo, nos orcamentos nem na fatura.
/// </summary>
public sealed class ProjectedRecurrence
{
    public Guid RecurrenceId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid AccountId { get; set; }

    /// <summary><c>Personal</c>/<c>Business</c>, da conta da regra.</summary>
    public string? AccountScope { get; set; }

    /// <summary><c>Expense</c> ou <c>Income</c>.</summary>
    public string TransactionType { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = string.Empty;

    public Guid? CreditCardId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>Valor estimado da primeira vigencia (no v1, de todas as ocorrencias).</summary>
    public decimal Amount { get; set; }

    /// <summary>RJT-3 (ADR-0010): todas as vigencias, a primeira inclusive; substituidas a cada RecurrenceSaved.</summary>
    public List<ProjectedRecurrenceAmount> Amounts { get; set; } = [];

    public string Currency { get; set; } = string.Empty;

    /// <summary><c>Monthly</c> ou <c>Yearly</c>.</summary>
    public string Frequency { get; set; } = string.Empty;

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }

    /// <summary>RJT-8 (ADR-0010, decisao 8): mes do aniversario do reajuste; nulo = sem reajuste.</summary>
    public int? AdjustmentMonth { get; set; }

    /// <summary>RJT-8: taxa (%) que a previsao usa no aniversario ainda nao aplicado; nula = sem estimativa.</summary>
    public decimal? AdjustmentEstimatedRate { get; set; }

    public bool AdjustmentNoReduction { get; set; }

    /// <summary>RJT-9: indice do reajuste (<c>IPCA</c>, <c>INPC</c>, <c>IGPM</c>); nulo no percentual fixo.</summary>
    public string? AdjustmentIndex { get; set; }

    /// <summary>Sequencia do ultimo evento do feed aplicado a esta linha.</summary>
    public long LastEventSequence { get; set; }
}

/// <summary>
/// REC-4 (ADR-0008, decisao 4): periodo fechado (lancado ou pulado) de uma regra, do
/// <c>RecurrenceOccurrenceClosed</c>; o <c>RecurrenceOccurrenceReopened</c> o remove.
/// </summary>
public sealed class ProjectedRecurrenceOccurrence
{
    public Guid RecurrenceId { get; set; }

    /// <summary><c>yyyy-MM</c> (mensal) ou <c>yyyy</c> (anual).</summary>
    public string Period { get; set; } = string.Empty;

    public Guid OwnerUserId { get; set; }

    /// <summary><c>Launched</c> ou <c>Skipped</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid? TransactionId { get; set; }

    public long LastEventSequence { get; set; }
}

/// <summary>RJT-3: o valor estimado de uma regra a partir de um periodo.</summary>
public sealed class ProjectedRecurrenceAmount
{
    public string FromPeriod { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
