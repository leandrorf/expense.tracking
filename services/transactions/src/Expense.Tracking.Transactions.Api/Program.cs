using Expense.Tracking.Transactions.Api.Endpoints;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Infrastructure;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();

const string SpaCorsPolicy = "spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddTransactionsInfrastructure(builder.Configuration);
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).WithTags("Health");
app.MapTransactionsEndpoints();

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
