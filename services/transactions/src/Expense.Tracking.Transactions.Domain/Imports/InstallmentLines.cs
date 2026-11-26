using System.Globalization;
using System.Text.RegularExpressions;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>Compra parcelada ja lancada no cartao: o que a fatura do banco cobra em parcelas.</summary>
public sealed record InstallmentPurchase(Guid TransactionId, decimal Amount, int Installments, DateOnly OccurredOn);

/// <summary>
/// RCN-5 (ADR-0011, decisao 5): linha "n/m" da fatura do banco que e a parcela <c>n</c> de uma compra em <c>m</c>
/// vezes ja lancada no cartao. Ela nao vira compra nova: fica duplicada, apontando a compra. Casa quando o valor
/// e o da parcela <c>n</c> (a mesma divisao da fatura calculada: arredondado para baixo, a sobra na primeira) e a
/// compra e de antes da linha (ate <c>n</c> meses e 45 dias antes, ou ate 3 dias depois). Cada parcela de uma
/// compra casa uma vez so.
/// </summary>
public static partial class InstallmentLines
{
    public static IReadOnlyDictionary<int, Guid> Match(
        IEnumerable<(int Number, DateOnly Date, decimal Amount, string Description)> lines,
        IReadOnlyList<InstallmentPurchase> purchases)
    {
        var matches = new Dictionary<int, Guid>();
        var used = new HashSet<(Guid, int)>();
        foreach (var line in lines.OrderBy(l => l.Number))
        {
            // RCN-7: "ANUIDADE 3/12" e encargo, nao parcela de compra.
            if (CardChargeHints.Matches(line.Description) || InstallmentOf(line.Description) is not { } installment)
            {
                continue;
            }

            var purchase = purchases
                .Where(p => p.Installments == installment.Of && !used.Contains((p.TransactionId, installment.Number)))
                .Where(p => p.OccurredOn <= line.Date.AddDays(3)
                    && p.OccurredOn >= line.Date.AddMonths(-installment.Number).AddDays(-45))
                .Where(p => AmountOf(p.Amount, p.Installments, installment.Number) == line.Amount)
                .OrderBy(p => Math.Abs(p.OccurredOn.DayNumber - line.Date.DayNumber))
                .FirstOrDefault();
            if (purchase is not null)
            {
                used.Add((purchase.TransactionId, installment.Number));
                matches[line.Number] = purchase.TransactionId;
            }
        }

        return matches;
    }

    /// <summary>"PARCELA 03/10", "PARC 3/10", "PARC.03/12", "3/10" ou "PARCELA 3 DE 10" (n de 1 a m, m de 2 a 24).</summary>
    public static (int Number, int Of)? InstallmentOf(string description)
    {
        var match = InstallmentPattern().Match(description);
        if (!match.Success)
        {
            return null;
        }

        var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var of = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        return number >= 1 && of >= 2 && number <= of && of <= Transactions.Transaction.MaxInstallments ? (number, of) : null;
    }

    /// <summary>Valor da parcela <paramref name="number"/> (INV-3): arredondado para baixo; a sobra vai na primeira.</summary>
    public static decimal AmountOf(decimal total, int installments, int number)
    {
        var installment = Math.Floor(total * 100m / installments) / 100m;
        return number == 1 ? total - (installment * (installments - 1)) : installment;
    }

    [GeneratedRegex(@"(?:PARC(?:ELA)?\.?\s*)?(?<!\d)(?<n>\d{1,2})\s*(?:/|\s+DE\s+)\s*(?<m>\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentPattern();
}
