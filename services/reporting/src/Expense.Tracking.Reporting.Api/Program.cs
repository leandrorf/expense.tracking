using Expense.Tracking.Reporting.Api.Endpoints;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Infrastructure;
using Expense.Tracking.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();
builder.AddTelemetry();
builder.AddStandardHealthChecks()
    .AddDbContextCheck<ReportingDbContext>("database", tags: [ObservabilitySetup.ReadyTag]);

const string SpaCorsPolicy = "spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

// EVT-2/EVT-3: projecao alimentada pelo feed de eventos do transactions (ADR-0001); o resumo le dela.
builder.Services.AddReportingInfrastructure(builder.Configuration);
if (builder.Configuration.GetValue("Projection:Enabled", true))
{
    builder.Services.AddHostedService<ProjectionWorker>();
}
builder.Services.AddReportingAuthentication(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Expense.Tracking.Reporting.Application.Abstractions.IUserTokenAccessor, HttpContextUserTokenAccessor>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(SpaCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCorrelationId();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(SpaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapReportsEndpoints();
app.MapBudgetsEndpoints();
app.MapInvoicesEndpoints();
app.MapForecastEndpoints();
app.MapRemindersEndpoints();

await InitializeDatabaseAsync(app);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

    if (db.Database.IsRelational())
    {
        await ApplyMigrationsWithRetryAsync(db, app.Logger);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

static async Task ApplyMigrationsWithRetryAsync(ReportingDbContext db, ILogger logger)
{
    const int maxAttempts = 10;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(ex, "Banco indisponivel (tentativa {Attempt}/{Max}). Retentando...", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

// Necessario para WebApplicationFactory nos testes de integracao.
public partial class Program;
