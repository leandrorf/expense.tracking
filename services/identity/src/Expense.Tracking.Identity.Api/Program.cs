using Expense.Tracking.Identity.Api.Endpoints;
using Expense.Tracking.Identity.Api.Setup;
using Expense.Tracking.Identity.Infrastructure;
using Expense.Tracking.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();
builder.AddTelemetry();
builder.AddStandardHealthChecks()
    .AddDbContextCheck<IdentityDbContext>("database", tags: [ObservabilitySetup.ReadyTag]);

// IDX-2: origens de CORS vem so da configuracao de quem roda o Identity (sem padrao no codigo).
const string SpaCorsPolicy = "spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.Configure<LoginPageOptions>(builder.Configuration.GetSection(LoginPageOptions.SectionName));
builder.Services.AddIdentityAuthentication();
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
app.MapAccountEndpoints();
app.MapOAuthEndpoints();
app.MapDiscoveryEndpoints();

await InitializeDatabaseAsync(app);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

    if (db.Database.IsRelational())
    {
        await ApplyMigrationsWithRetryAsync(db, app.Logger);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    var seed = app.Configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
    if (app.Environment.IsDevelopment() || seed.Enabled)
    {
        await IdentitySeeder.SeedAsync(app.Services, seed);
    }
}

static async Task ApplyMigrationsWithRetryAsync(IdentityDbContext db, ILogger logger)
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
