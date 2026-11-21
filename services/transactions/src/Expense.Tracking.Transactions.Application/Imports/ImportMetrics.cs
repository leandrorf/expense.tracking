using System.Diagnostics.Metrics;
using Expense.Tracking.Transactions.Domain.Imports;

namespace Expense.Tracking.Transactions.Application.Imports;

/// <summary>
/// IMP-5 (DoD do epico IMP): contadores da importacao no pipeline da OBS. So contagens: nada do
/// conteudo do arquivo vira tag (ADR-0004, decisao 8).
/// </summary>
public sealed class ImportMetrics : IDisposable
{
    public const string MeterName = "Expense.Tracking.Transactions.Imports";

    private readonly Meter _meter;
    private readonly Counter<long> _lines;

    public ImportMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _lines = _meter.CreateCounter<long>(
            "transactions.imports.lines", unit: "{line}",
            description: "Linhas de importacao confirmadas, por resultado (imported ou duplicate) e formato.");
    }

    public void LinesConfirmed(ImportFormat format, int imported, int duplicate)
    {
        var formatTag = new KeyValuePair<string, object?>("format", format.ToString().ToLowerInvariant());
        _lines.Add(imported, formatTag, new KeyValuePair<string, object?>("result", "imported"));
        _lines.Add(duplicate, formatTag, new KeyValuePair<string, object?>("result", "duplicate"));
    }

    public void Dispose() => _meter.Dispose();
}
