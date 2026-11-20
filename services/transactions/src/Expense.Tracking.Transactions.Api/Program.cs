using Expense.Tracking.Transactions.Api.Endpoints;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Infrastructure;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Expense.Tracking.Transactions.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();
builder.AddTelemetry();
builder.AddStandardHealthChecks()
    .AddDbContextCheck<TransactionsDbContext>("database", tags: [ObservabilitySetup.ReadyTag]);

const string SpaCorsPolicy = "spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddTransactionsInfrastructure(builder.Configuration);
// EVT-4: completa o escopo PF/PJ dos eventos de lancamentos anteriores ao outbox.
if (builder.Configuration.GetValue(DependencyInjection.LegacyScopeBackfillEnabledKey, true))
{
    builder.Services.AddHostedService<AccountScopeBackfillWorker>();
}
builder.Services.AddTransactionsAuthentication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserTokenAccessor, HttpContextUserTokenAccessor>();
builder.Services.AddScoped<ICorrelationContext, HttpContextCorrelationContext>();
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
app.MapTransactionsEndpoints();
app.MapEventsEndpoints();

await InitializeDatabaseAsync(app);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();

    if (db.Database.IsRelational())
    {
        await ApplyMigrationsWithRetryAsync(db, app.Logger);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    // EVT-1: lancamentos anteriores ao outbox ganham seu TransactionRegistered (idempotente).
    var backfilled = await OutboxBackfill.RunAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    if (backfilled > 0)
    {
        app.Logger.LogInformation("Carga inicial do outbox: {Count} lancamentos ganharam TransactionRegistered.", backfilled);
    }
}

static async Task ApplyMigrationsWithRetryAsync(TransactionsDbContext db, ILogger logger)
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
