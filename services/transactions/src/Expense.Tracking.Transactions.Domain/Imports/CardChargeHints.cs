using System.Text.RegularExpressions;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>
/// RCN-7 (ADR-0011, decisao 5): encargo cobrado pelo banco na fatura do cartao (IOF, juros, multa, mora, anuidade,
/// tarifa, encargos), reconhecido pela descricao. Vem com a categoria <see cref="Category"/> sugerida e nunca e
/// tomado por parcela de compra ("ANUIDADE 3/12").
/// </summary>
public static partial class CardChargeHints
{
    public const string Category = "Encargos do cartão";

    public static bool Matches(string description) => ChargePattern().IsMatch(description);

    [GeneratedRegex(@"\b(IOF|JUROS|MULTA|MORA|ANUIDADE|TARIFA|ENCARGOS?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChargePattern();
}
