using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>
/// RCN-2 (ADR-0011, decisao 7): a fatura de um cartao conferida com a do banco. Guarda o total do banco e o
/// total do app no momento; conferir com diferenca e permitido (a diferenca fica registrada). Uma por
/// (dono, cartao, mes de vencimento); conferir de novo substitui.
/// </summary>
public sealed class InvoiceReconciliation
{
    /// <summary>Maior total aceito, em modulo.</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    public static readonly Error BankTotalInvalid = Error.Validation(
        "reconciliation.bank_total_invalid", "Informe o total da fatura do banco, com ate 2 casas decimais.");

    private InvoiceReconciliation()
    {
    }

    public Guid OwnerUserId { get; private set; }

    public Guid CardId { get; private set; }

    /// <summary>Mes de vencimento da fatura (dia 1).</summary>
    public DateOnly DueMonth { get; private set; }

    /// <summary>Total da fatura do banco, como o usuario informou.</summary>
    public decimal BankTotal { get; private set; }

    /// <summary>Total da fatura do app quando foi conferida.</summary>
    public decimal AppTotal { get; private set; }

    public DateTimeOffset ReconciledAtUtc { get; private set; }

    /// <summary>Diferenca aceita na conferencia: banco - app.</summary>
    public decimal Difference => BankTotal - AppTotal;

    public static Result<InvoiceReconciliation> Create(
        Guid ownerUserId, Guid cardId, DateOnly dueMonth, decimal bankTotal, decimal appTotal, DateTimeOffset nowUtc)
    {
        var reconciliation = new InvoiceReconciliation
        {
            OwnerUserId = ownerUserId,
            CardId = cardId,
            DueMonth = new DateOnly(dueMonth.Year, dueMonth.Month, 1),
        };
        var updated = reconciliation.Update(bankTotal, appTotal, nowUtc);
        return updated.IsFailure ? Result.Failure<InvoiceReconciliation>(updated.Error) : reconciliation;
    }

    /// <summary>Confere de novo: troca o total do banco, o do app e a data.</summary>
    public Result Update(decimal bankTotal, decimal appTotal, DateTimeOffset nowUtc)
    {
        if (decimal.Round(bankTotal, 2) != bankTotal || Math.Abs(bankTotal) > MaxAmount)
        {
            return Result.Failure(BankTotalInvalid);
        }

        BankTotal = bankTotal;
        AppTotal = appTotal;
        ReconciledAtUtc = nowUtc;
        return Result.Success();
    }
}
