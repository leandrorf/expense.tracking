using System.Diagnostics.Metrics;

namespace Expense.Tracking.Reporting.Application.Projection;

/// <summary>
/// EVT-3: ultimo estado conhecido da projecao, atualizado pelo worker a cada leitura do feed, e
/// exposto como metricas (pipeline da OBS-3):
/// <list type="bullet">
/// <item><c>reporting.projection.lag</c>: eventos gravados no outbox do transactions e ainda nao
/// aplicados (head do feed menos o checkpoint).</item>
/// <item><c>reporting.projection.checkpoint</c>: ultima sequencia aplicada.</item>
/// </list>
/// Antes da primeira leitura bem-sucedida nada e medido: um atraso desconhecido nao vira zero.
/// </summary>
public sealed class ProjectionStatus : IDisposable
{
    public const string LagInstrument = "reporting.projection.lag";
    public const string CheckpointInstrument = "reporting.projection.checkpoint";

    private readonly Meter _meter;
    private Snapshot? _last;

    public ProjectionStatus(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(ProjectionTelemetry.SourceName);
        _meter.CreateObservableGauge(
            LagInstrument,
            () => Observe(s => s.Lag),
            unit: "{event}",
            description: "Eventos do feed do transactions ainda nao aplicados na projecao.");
        _meter.CreateObservableGauge(
            CheckpointInstrument,
            () => Observe(s => s.Checkpoint),
            unit: "{event}",
            description: "Ultima sequencia do feed aplicada na projecao.");
    }

    public long? Lag => Volatile.Read(ref _last)?.Lag;

    public void Record(ProjectionBatchResult result) =>
        Volatile.Write(ref _last, new Snapshot(result.Checkpoint, result.Lag));

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> Observe(Func<Snapshot, long> select)
    {
        var last = Volatile.Read(ref _last);
        return last is null ? [] : [new Measurement<long>(select(last))];
    }

    private sealed record Snapshot(long Checkpoint, long Lag);
}
