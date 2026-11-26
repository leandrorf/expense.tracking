using System.Text.RegularExpressions;

namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>
/// RCN-7 (ADR-0011, decisao 5): encargo cobrado pelo banco na fatura (IOF, juros, multa, mora, anuidade, tarifa,
/// encargos), pela descricao. A conciliacao o marca e nao o casa como parcela ("ANUIDADE 3/12").
/// </summary>
public static partial class CardChargeHints
{
    public static bool Matches(string? description) => description is not null && ChargePattern().IsMatch(description);

    [GeneratedRegex(@"\b(IOF|JUROS|MULTA|MORA|ANUIDADE|TARIFA|ENCARGOS?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChargePattern();
}
