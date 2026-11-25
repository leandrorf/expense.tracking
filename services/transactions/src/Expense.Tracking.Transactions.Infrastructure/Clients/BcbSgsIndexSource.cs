using System.Globalization;
using System.Net;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Recurrences;

namespace Expense.Tracking.Transactions.Infrastructure.Clients;

/// <summary>
/// RJT-6 (ADR-0010, decisao 6): API de series temporais (SGS) do Banco Central, sem autenticacao:
/// <c>GET dados/serie/bcdata.sgs.{serie}/dados?formato=json&amp;dataInicial=dd/MM/yyyy&amp;dataFinal=dd/MM/yyyy</c>
/// devolve <c>[{ "data": "01/10/2026", "valor": "0.14" }]</c>; intervalo sem dados = 404. Unica saida do
/// sistema para a internet: so vao o codigo da serie e as datas.
/// </summary>
public sealed class BcbSgsIndexSource(HttpClient httpClient) : IEconomicIndexSource
{
    public async Task<IReadOnlyList<IndexMonthRate>> FetchAsync(
        EconomicIndex index, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"dados/serie/bcdata.sgs.{EconomicIndexValue.SgsSeriesOf(index)}/dados?formato=json&dataInicial={from:dd/MM/yyyy}&dataFinal={to:dd/MM/yyyy}");
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return Parse(await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken));
    }

    /// <summary>Le a resposta; qualquer item fora do formato invalida a resposta inteira.</summary>
    public static IReadOnlyList<IndexMonthRate> Parse(JsonDocument document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Resposta do SGS fora do formato: era esperada uma lista.");
        }

        var rates = new List<IndexMonthRate>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("valor", out var valor) || valor.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(data.GetString(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(valor.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var rate))
            {
                throw new InvalidDataException($"Item da resposta do SGS fora do formato: {item.GetRawText()}");
            }

            rates.Add(new IndexMonthRate(EconomicIndexValue.MonthOf(date), rate));
        }

        return rates;
    }
}
