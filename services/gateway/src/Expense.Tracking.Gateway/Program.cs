using Expense.Tracking.Gateway.Setup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();
builder.AddTelemetry();
builder.AddStandardHealthChecks();

const string SpaCorsPolicy = "spa";
const string AuthenticatedPolicy = "authenticated";

var authority = builder.Configuration["Jwt:Authority"] ?? "http://localhost:5080";
var audience = builder.Configuration["Jwt:Audience"] ?? "expense-tracking-api";
var metadataAddress = builder.Configuration["Jwt:MetadataAddress"];
if (string.IsNullOrWhiteSpace(metadataAddress))
{
    metadataAddress = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

// Reverse proxy (YARP) carregado da secao "ReverseProxy".
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Validacao do token na borda (mesma config dos servicos de recurso).
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MetadataAddress = metadataAddress;
        options.RequireHttpsMetadata = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser());

builder.Services.AddCors(options =>
{
    options.AddPolicy(SpaCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCorrelationId();

// IMP-4 (ADR-0004, decisao 8): o envio de extrato e fatura tem limite proprio de corpo ja na
// borda (arquivo de 5 MB mais o multipart), em vez dos 30 MB padrao do Kestrel. Com
// Content-Length acima do limite, recusa sem repassar nada ao transactions; sem ele (chunked), o
// Kestrel interrompe a leitura no limite.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/imports"))
    {
        if (context.Request.ContentLength > ImportsLimits.MaxRequestBytes)
        {
            await Results.Problem(
                title: "import.file_too_large", detail: "O arquivo passa do limite de 5 MB.",
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
            return;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = ImportsLimits.MaxRequestBytes;
        }
    }

    await next();
});

app.UseCors(SpaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();

// EVT-4: endpoints internos dos servicos (ex.: /internal/account-scopes do accounts) nao saem
// pela borda. Sem isto, o catch-all /api/** -> accounts os exporia em /api/internal/**. A rota
// literal e mais especifica que o catch-all do YARP, entao vence no roteamento.
app.Map("/api/internal/{**rest}", () => Results.NotFound());

// Encaminha as rotas configuradas (protegidas pela policy definida em cada rota).
app.MapReverseProxy();

app.Run();

// Necessario para WebApplicationFactory nos testes de integracao.
public partial class Program;

/// <summary>Limite do corpo em /api/imports: o mesmo do servico transactions (IMP-4).</summary>
public static class ImportsLimits
{
    public const long MaxRequestBytes = 5 * 1024 * 1024 + 64 * 1024;
}
