using Expense.Tracking.Reporting.Api.Endpoints;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

const string SpaCorsPolicy = "spa";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddReportingInfrastructure(builder.Configuration);
builder.Services.AddReportingAuthentication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserTokenAccessor, HttpContextUserTokenAccessor>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(SpaCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(SpaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).WithTags("Health");
app.MapReportsEndpoints();

app.Run();

// Necessario para WebApplicationFactory nos testes de integracao.
public partial class Program;
