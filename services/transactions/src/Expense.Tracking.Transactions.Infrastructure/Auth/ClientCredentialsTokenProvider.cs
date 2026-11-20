using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Expense.Tracking.Transactions.Infrastructure.Auth;

/// <summary>Credenciais do transactions como cliente de servico (client credentials).</summary>
public sealed record ServiceClientOptions(Uri TokenEndpoint, string ClientId, string ClientSecret, string Scope);

/// <summary>Fornece um access token de servico para chamadas sem usuario (ex.: o backfill de escopos).</summary>
public interface IServiceTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// EVT-4 (mesma implementacao do reporting, EVT-2): obtem token por client credentials no
/// identity e o reaproveita ate perto de expirar (renova com <see cref="RefreshBefore"/> de folga). Uma chamada por vez ao identity. E singleton
/// (o cache vive nele), por isso cria o HttpClient pela factory a cada renovacao em vez de
/// segurar um so para sempre.
/// </summary>
public sealed class ClientCredentialsTokenProvider(IHttpClientFactory httpClientFactory, ServiceClientOptions options, TimeProvider timeProvider)
    : IServiceTokenProvider, IDisposable
{
    public const string HttpClientName = "identity-token";
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAtUtc;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsValid())
        {
            return _token!;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsValid())
            {
                return _token!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret,
                    ["scope"] = options.Scope,
                }),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Sem o corpo: a resposta de erro do identity pode ecoar dados do cliente.
                throw new HttpRequestException(
                    $"O identity recusou o token de servico ({(int)response.StatusCode}).", null, response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Resposta de token vazia.");

            _token = body.AccessToken;
            _expiresAtUtc = timeProvider.GetUtcNow().AddSeconds(body.ExpiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private bool IsValid() => _token is not null && timeProvider.GetUtcNow() < _expiresAtUtc - RefreshBefore;

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Coloca o token de servico nas chamadas do client que o usa.</summary>
internal sealed class ServiceTokenHandler(IServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetTokenAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
