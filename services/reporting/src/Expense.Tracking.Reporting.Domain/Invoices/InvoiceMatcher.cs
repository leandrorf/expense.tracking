using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>
/// Uma linha da fatura do banco. <see cref="Amount"/> com sinal da fatura: positivo e compra (debito),
/// negativo e credito (estorno). <see cref="KnownTransactionId"/> e o lancamento que a linha ja e (importada
/// antes ou repetida), quando o lote sabe.
/// </summary>
public sealed record BankLine(Guid LineId, int Number, DateOnly Date, decimal Amount, string Description, Guid? KnownTransactionId);

/// <summary>Como o par foi achado (ADR-0011, decisao 3).</summary>
public enum MatchRule
{
    Key = 1,
    Installment = 2,
    AmountAndDate = 3,
}

/// <summary>Grupo de cada linha ou item no resultado (ADR-0011, decisao 4).</summary>
public enum ReconciliationGroup
{
    Matched = 1,
    DifferentAmount = 2,
    OnlyInBank = 3,
    OnlyInApp = 4,
}

/// <summary>Uma entrada do resultado: a linha do banco, o item do app, ou os dois quando casaram.</summary>
public sealed record ReconciliationEntry(ReconciliationGroup Group, BankLine? Line, InvoiceItem? Item, MatchRule? Rule)
{
    /// <summary>Quanto a entrada explica da diferenca banco - app.</summary>
    public decimal Explains => (Line?.Amount ?? 0m) - (Item?.Amount ?? 0m);
}

/// <summary>
/// RCN-4 (ADR-0011, decisoes 3 e 4): casa as linhas da fatura do banco com os itens da fatura calculada, nesta
/// ordem e cada um uma vez so: (1) mesma chave (a linha ja e aquele lancamento); (2) parcela "n/m" com o
/// valor da parcela (sem olhar a data; com valor diferente, so se a descricao for parecida); (3) mesmo valor e
/// data da compra ate <see cref="DateWindowDays"/> dias, a mais proxima primeiro. Funcao pura.
/// </summary>
public static partial class InvoiceMatcher
{
    public const int DateWindowDays = 3;

    public static IReadOnlyList<ReconciliationEntry> Match(IEnumerable<BankLine> bankLines, IEnumerable<InvoiceItem> invoiceItems)
    {
        var lines = bankLines.OrderBy(l => l.Number).ToList();
        var items = invoiceItems.ToList();
        var entries = new List<ReconciliationEntry>();
        var freeLines = new HashSet<BankLine>(lines);
        var freeItems = new HashSet<InvoiceItem>(items);

        void Pair(BankLine line, InvoiceItem item, MatchRule rule)
        {
            freeLines.Remove(line);
            freeItems.Remove(item);
            var group = line.Amount == item.Amount ? ReconciliationGroup.Matched : ReconciliationGroup.DifferentAmount;
            entries.Add(new ReconciliationEntry(group, line, item, rule));
        }

        // (1) Mesma chave.
        foreach (var line in lines.Where(l => l.KnownTransactionId is not null))
        {
            if (freeItems.FirstOrDefault(i => i.TransactionId == line.KnownTransactionId) is { } item)
            {
                Pair(line, item, MatchRule.Key);
            }
        }

        // (2) Parcela "n/m": primeiro com o mesmo valor; depois, valor diferente com descricao parecida.
        foreach (var line in lines.Where(freeLines.Contains).ToList())
        {
            if (InstallmentOf(line.Description) is not { } installment)
            {
                continue;
            }

            var candidates = freeItems
                .Where(i => i.Installment == installment.Number && i.Installments == installment.Of)
                .OrderByDescending(i => Similarity(line.Description, i.Description))
                .ThenBy(i => Math.Abs(i.PurchaseDate.DayNumber - line.Date.DayNumber))
                .ToList();
            var chosen = candidates.FirstOrDefault(i => i.Amount == line.Amount)
                ?? candidates.FirstOrDefault(i => Similarity(line.Description, i.Description) > 0);
            if (chosen is not null)
            {
                Pair(line, chosen, MatchRule.Installment);
            }
        }

        // (3) Mesmo valor e data ate 3 dias, a mais proxima primeiro (desempate: descricao parecida).
        var byCloseness = (
                from line in freeLines
                from item in freeItems
                let days = Math.Abs(item.PurchaseDate.DayNumber - line.Date.DayNumber)
                where item.Amount == line.Amount && days <= DateWindowDays
                select (line, item, days, similarity: Similarity(line.Description, item.Description)))
            .OrderBy(c => c.days)
            .ThenByDescending(c => c.similarity)
            .ThenBy(c => c.line.Number)
            .ToList();
        foreach (var (line, item, _, _) in byCloseness)
        {
            if (freeLines.Contains(line) && freeItems.Contains(item))
            {
                Pair(line, item, MatchRule.AmountAndDate);
            }
        }

        entries.AddRange(lines.Where(freeLines.Contains).Select(l => new ReconciliationEntry(ReconciliationGroup.OnlyInBank, l, null, null)));
        entries.AddRange(items.Where(freeItems.Contains).Select(i => new ReconciliationEntry(ReconciliationGroup.OnlyInApp, null, i, null)));
        return entries;
    }

    /// <summary>"PARCELA 03/10", "PARC 3/10", "3/10" ou "PARCELA 3 DE 10" (n de 1 a m, m de 2 a 99).</summary>
    public static (int Number, int Of)? InstallmentOf(string description)
    {
        var match = InstallmentPattern().Match(description);
        if (!match.Success)
        {
            return null;
        }

        var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var of = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        return number >= 1 && of >= 2 && number <= of ? (number, of) : null;
    }

    /// <summary>Palavras em comum (minusculas, sem acento, sem numeros, com 3 letras ou mais).</summary>
    public static int Similarity(string? a, string? b)
    {
        var left = Words(a);
        return left.Count == 0 ? 0 : Words(b).Count(left.Contains);
    }

    private static HashSet<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            builder.Append(Unaccent(c) is var plain && char.IsAsciiLetterLower(plain) ? plain : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToHashSet(StringComparer.Ordinal);
    }

    // Sem string.Normalize (os servicos rodam com InvariantGlobalization): o mapeamento e explicito.
    private static char Unaccent(char c) => c switch
    {
        'á' or 'à' or 'â' or 'ã' or 'ä' => 'a',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ç' => 'c',
        _ => c,
    };

    [GeneratedRegex(@"(?:PARC(?:ELA)?\.?\s*)?(?<!\d)(?<n>\d{1,2})\s*(?:/|\s+DE\s+)\s*(?<m>\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentPattern();
}
