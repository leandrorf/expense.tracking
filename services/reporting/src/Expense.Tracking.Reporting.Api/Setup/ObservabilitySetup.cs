using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Expense.Tracking.Reporting.Api.Setup;

/// <summary>
/// OBS-1: logs estruturados (JSON) + correlation id por request.
/// O id vem do header <c>X-Correlation-Id</c> (se o chamador enviar), senao do trace id
/// do <see cref="Activity"/> atual, senao de um GUID novo. Ele e ecoado na resposta e
/// injetado num escopo de log ambiente, entao todas as linhas de log da request o carregam.
/// </summary>
public static class ObservabilitySetup
{
    public const string CorrelationIdHeader = "X-Correlation-Id";
    public const string ServiceName = "expense-tracking-reporting";
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string HealthPath = "/health";
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
    /// OBS-2: tracing distribuido com OpenTelemetry. Cria um span por request recebida
    /// (ASP.NET Core) e um span filho por chamada HTTP de saida (HttpClient/YARP). O contexto
    /// viaja no header W3C <c>traceparent</c>, entao gateway -> transactions -> accounts formam
    /// um unico trace. Os spans so sao exportados (OTLP) quando
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> esta configurado; sem ele (testes, dev local) o
    /// tracing segue ativo e o trace id continua alimentando o correlation id e os logs.
    /// </summary>
    public static WebApplicationBuilder AddDistributedTracing(this WebApplicationBuilder builder)
    {
        var otlpEndpoint = builder.Configuration[OtlpEndpointKey];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                        // Health checks (docker/CI) so gerariam ruido nos traces.
                        options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath))
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
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
