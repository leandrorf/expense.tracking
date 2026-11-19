using System.Net;

namespace Expense.Tracking.Accounts.IntegrationTests;

/// <summary>OBS-1: toda resposta carrega o correlation id; um id enviado pelo chamador e preservado.</summary>
public sealed class ObservabilityTests(AccountsApiFactory factory) : IClassFixture<AccountsApiFactory>
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private readonly AccountsApiFactory _factory = factory;

    [Fact]
    public async Task Response_Always_Carries_A_CorrelationId_Header()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdHeader, out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.Single()));
    }

    [Fact]
    public async Task Response_Echoes_The_Inbound_CorrelationId()
    {
        var client = _factory.CreateClient();
        const string correlationId = "test-correlation-id-123";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdHeader, correlationId);

        var response = await client.SendAsync(request);

        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationIdHeader).Single());
    }
}
