using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Expense.Tracking.Reporting.Domain.Budgets;

/// <summary>
/// ADR-0002: chave de correspondencia de categoria, aplicada ao orcamento e aos lancamentos.
/// Ignora espacos nas pontas, espacos repetidos, maiusculas e acentos, sem depender da collation
/// do banco ("Alimentação", "alimentacao " e "ALIMENTACAO" viram "alimentacao").
/// </summary>
/// <remarks>
/// Os servicos rodam com <c>InvariantGlobalization</c>, em que <c>string.Normalize(FormD)</c> nao
/// decompoe acentos. Por isso as letras acentuadas latinas sao mapeadas explicitamente; marcas
/// combinantes soltas (texto ja decomposto) sao descartadas.
/// </remarks>
public static class CategoryKey
{
    private static readonly FrozenDictionary<char, char> Unaccented = BuildMap(
        ("àáâãäåā", 'a'), ("çćč", 'c'), ("ď", 'd'), ("èéêëēęě", 'e'), ("ìíîïī", 'i'),
        ("ñńň", 'n'), ("òóôõöøō", 'o'), ("ř", 'r'), ("śš", 's'), ("ť", 't'),
        ("ùúûüūů", 'u'), ("ýÿ", 'y'), ("źżž", 'z'));

    public static string Normalize(string category)
    {
        var trimmed = category.Trim();
        var builder = new StringBuilder(trimmed.Length);
        var previousWasSpace = false;

        foreach (var original in trimmed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(original) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(original))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            var lower = char.ToLowerInvariant(original);
            builder.Append(Unaccented.GetValueOrDefault(lower, lower));
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    private static FrozenDictionary<char, char> BuildMap(params (string Accented, char Plain)[] groups) =>
        groups
            .SelectMany(g => g.Accented.Select(c => KeyValuePair.Create(c, g.Plain)))
            .ToFrozenDictionary();
}
