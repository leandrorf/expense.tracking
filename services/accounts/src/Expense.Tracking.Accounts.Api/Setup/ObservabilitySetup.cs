using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Expense.Tracking.Accounts.Api.Setup;

/// <summary>
/// OBS-1: logs estruturados (JSON) + correlation id por request.
/// O id vem do header <c>X-Correlation-Id</c> (se o chamador enviar), senao do trace id
/// do <see cref="Activity"/> atual, senao de um GUID novo. Ele e ecoado na resposta e
/// injetado num escopo de log ambiente, entao todas as linhas de log da request o carregam.
/// </summary>
public static class ObservabilitySetup
{
    public const string CorrelationIdHeader = "X-Correlation-Id";
    public const string ServiceName = "expense-tracking-accounts";
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string ReadyTag = "ready";
    private const string HealthPath = "/health";
    private const string RuntimeMeterName = "System.Runtime";
    private const string EmptyTraceId = "00000000000000000000000000000000";

    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });
        return builder;
    }

    /// <summary>
    /// OBS-2/OBS-3: telemetria com OpenTelemetry, com o mesmo recurso
    /// (<c>expense-tracking-&lt;servico&gt;</c>) para traces e metricas.
    /// <list type="bullet">
    /// <item>Traces: um span por request recebida (ASP.NET Core) e um span filho por chamada HTTP
    /// de saida (HttpClient/YARP). O contexto viaja no header W3C <c>traceparent</c>, entao
    /// gateway -> transactions -> accounts formam um unico trace.</item>
    /// <item>Metricas: duracao/contagem de requests recebidas e enviadas
    /// (<c>http.server.request.duration</c>, <c>http.client.request.duration</c>) e runtime do
    /// .NET (GC, memoria, threads), pelo meter nativo <c>System.Runtime</c>.</item>
    /// </list>
    /// Tudo so e exportado (OTLP, para o OpenTelemetry Collector) quando
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> esta configurado; sem ele (testes, dev local) a
    /// instrumentacao segue ativa e o trace id continua alimentando o correlation id e os logs.
    /// </summary>
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        var exportEnabled = !string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                        // Health checks (docker/CI) so gerariam ruido nos traces.
                        options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath))
                    .AddHttpClientInstrumentation();

                if (exportEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(RuntimeMeterName);

                if (exportEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return builder;
    }

    /// <summary>
    /// OBS-3: registra os health checks do servico. Checks com a tag <see cref="ReadyTag"/>
    /// (ex.: o banco) entram so na prontidao; a vivacidade nunca roda checks.
    /// </summary>
    public static IHealthChecksBuilder AddStandardHealthChecks(this WebApplicationBuilder builder) =>
        builder.Services.AddHealthChecks();

    /// <summary>
    /// OBS-3: endpoints de health padronizados, publicos e com resposta JSON.
    /// <list type="bullet">
    /// <item><c>/health/live</c>: o processo responde (nenhum check roda).</item>
    /// <item><c>/health/ready</c>: as dependencias do proprio servico (checks com a tag
    /// <see cref="ReadyTag"/>) estao ok. 503 quando alguma falha.</item>
    /// <item><c>/health</c>: alias de <c>/health/live</c>, mantido por compatibilidade.</item>
    /// </list>
    /// </summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        var live = new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealthJsonAsync };
        var ready = new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteHealthJsonAsync,
        };

        app.MapHealthChecks(HealthPath, live);
        app.MapHealthChecks(HealthPath + "/live", live);
        app.MapHealthChecks(HealthPath + "/ready", ready);

        return app;
    }

    // So nome, status e duracao: detalhes de excecao (ex.: string de conexao) nao vazam.
    private static Task WriteHealthJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                }),
        });
    }

    public static WebApplication UseCorrelationId(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Expense.Tracking.Observability");

        app.Use(async (context, next) =>
        {
            var correlationId = ResolveCorrelationId(context);

            // Disponibiliza o id na propria request (o gateway/YARP reencaminha o header
            // e os typed clients o leem) e garante o eco na resposta.
            context.Request.Headers[CorrelationIdHeader] = correlationId;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationIdHeader] = correlationId;
                return Task.CompletedTask;
            });

            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await next(context);
            }
        });

        return app;
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationIdHeader].ToString();
        if (!string.IsNullOrWhiteSpace(incoming))
        {
            return incoming;
        }

        var traceId = Activity.Current?.TraceId.ToString();
        return !string.IsNullOrEmpty(traceId) && traceId != EmptyTraceId
            ? traceId
            : Guid.NewGuid().ToString("N");
    }
}
