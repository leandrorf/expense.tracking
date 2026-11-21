using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>
/// Chave de deduplicacao de linha sem identificador do banco (CSV, ou OFX sem FITID): hash de
/// data, valor com sinal e descricao normalizada, mais a ordem da linha entre as identicas do
/// arquivo (ADR-0004, decisao 3). Dois cafes iguais no mesmo dia viram "...-1" e "...-2": sao dois
/// gastos, e reimportar o mesmo arquivo gera as mesmas duas chaves.
/// </summary>
public static class StatementFingerprint
{
    public const string Prefix = "fp-";

    /// <summary>
    /// Completa a <see cref="StatementLine.ExternalId"/> das linhas validas que nao tem uma.
    /// Linhas invalidas e as que ja tem chave (FITID) ficam como estao.
    /// </summary>
    public static IReadOnlyList<StatementLine> Assign(IReadOnlyList<StatementLine> lines)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        return lines.Select(line =>
        {
            if (!line.IsValid || line.ExternalId is not null)
            {
                return line;
            }

            var hash = Hash(line.Date!.Value, line.Amount!.Value, line.Description);
            var occurrence = occurrences.GetValueOrDefault(hash) + 1;
            occurrences[hash] = occurrence;
            return line with { ExternalId = $"{Prefix}{hash}-{occurrence}" };
        }).ToList();
    }

    /// <summary>Descricao comparavel: maiusculas e espacos simples. "Uber *Trip" = "UBER  *TRIP".</summary>
    public static string NormalizeDescription(string description) =>
        ImportText.NormalizeSpaces(description).ToUpperInvariant();

    private static string Hash(DateOnly date, decimal amount, string description)
    {
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{date:yyyy-MM-dd}|{amount:0.00}|{NormalizeDescription(description)}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
    }
}
