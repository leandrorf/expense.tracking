using System.Text;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>Cartao da conta do extrato, candidato a ter a fatura paga por uma linha (so o que a sugestao usa).</summary>
public sealed record InvoiceCardHint(Guid CardId, int DueDay);

/// <summary>
/// INV-11 (ADR-0005, decisao 10): pistas no memo de um extrato de conta de que a saida e o
/// pagamento da fatura de um cartao ("PAG FATURA", "PAGAMENTO FATURA", "PGTO CARTAO"...). So
/// sugerem: o usuario troca a linha para lancamento comum na previa. Sem diferenca de maiusculas
/// nem de acentos, palavras inteiras.
/// </summary>
public static class InvoicePaymentHints
{
    private static readonly string[] Phrases =
    [
        "PAG FATURA", "PAGTO FATURA", "PGTO FATURA", "PAGAMENTO FATURA", "PAGAMENTO DE FATURA",
        "PAG CARTAO", "PAGTO CARTAO", "PGTO CARTAO", "PAGAMENTO CARTAO", "PAGAMENTO DE CARTAO",
    ];

    public static bool Matches(string? memo)
    {
        if (string.IsNullOrWhiteSpace(memo))
        {
            return false;
        }

        var normalized = $" {Normalize(memo)} ";
        return Phrases.Any(phrase => normalized.Contains($" {phrase} ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Fatura que o pagamento provavelmente quita: a de vencimento mais proximo da data paga (o mes
    /// anterior, o mesmo ou o seguinte). Devolve o mes de vencimento (dia 1).
    /// </summary>
    public static DateOnly SuggestDueMonth(DateOnly paidOn, int dueDay)
    {
        var month = new DateOnly(paidOn.Year, paidOn.Month, 1);
        return new[] { month.AddMonths(-1), month, month.AddMonths(1) }
            .MinBy(m => Math.Abs(new DateOnly(m.Year, m.Month, Math.Min(dueDay, DateTime.DaysInMonth(m.Year, m.Month))).DayNumber - paidOn.DayNumber));
    }

    /// <summary>
    /// Maiusculas, sem acentos (mapa explicito: o servico roda com globalizacao invariante, sem a
    /// decomposicao Unicode), e qualquer coisa que nao seja letra ou digito vira um espaco.
    /// </summary>
    private static string Normalize(string memo)
    {
        var builder = new StringBuilder(memo.Length);
        var lastWasSpace = true;
        foreach (var raw in memo)
        {
            var c = Unaccent(char.ToUpperInvariant(raw));
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static char Unaccent(char c) => c switch
    {
        'Á' or 'À' or 'Â' or 'Ã' or 'Ä' => 'A',
        'É' or 'È' or 'Ê' or 'Ë' => 'E',
        'Í' or 'Ì' or 'Î' or 'Ï' => 'I',
        'Ó' or 'Ò' or 'Ô' or 'Õ' or 'Ö' => 'O',
        'Ú' or 'Ù' or 'Û' or 'Ü' => 'U',
        'Ç' => 'C',
        _ => c,
    };
}
