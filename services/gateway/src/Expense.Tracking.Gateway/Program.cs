using Expense.Tracking.Gateway.Setup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging();

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

app.UseCors(SpaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Encaminha as rotas configuradas (protegidas pela policy definida em cada rota).
app.MapReverseProxy();

app.Run();

// Necessario para WebApplicationFactory nos testes de integracao.
public partial class Program;
