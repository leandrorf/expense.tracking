using System.Diagnostics;
using System.Text.Json;

namespace Expense.Tracking.Gateway.Setup;

/// <summary>
/// OBS-1: logs estruturados (JSON) + correlation id por request na borda.
/// O id vem do header <c>X-Correlation-Id</c> (se o chamador enviar), senao do trace id
/// do <see cref="Activity"/> atual, senao de um GUID novo. Como o id e escrito de volta
/// na request antes do <c>MapReverseProxy</c>, o YARP o reencaminha aos servicos de recurso,
/// amarrando os logs de toda a cadeia ao mesmo correlation id.
/// </summary>
public static class ObservabilitySetup
{
    public const string CorrelationIdHeader = "X-Correlation-Id";
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

    public static WebApplication UseCorrelationId(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Expense.Tracking.Observability");

        app.Use(async (context, next) =>
        {
            var correlationId = ResolveCorrelationId(context);

            // Escreve o id na request para que o YARP o reencaminhe aos backends.
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
