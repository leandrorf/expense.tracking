using System.Net;
using System.Text;
using Expense.Tracking.Reporting.Infrastructure.Auth;

namespace Expense.Tracking.Reporting.IntegrationTests;

/// <summary>EVT-2: token de servico por client credentials, reaproveitado ate perto de expirar.</summary>
public sealed class ServiceTokenProviderTests
{
    private static readonly ServiceClientOptions Options = new(
        new Uri("http://identity.test/oauth/token"), "expense-tracking-service", "s3cret", "events.read");

    [Fact]
    public async Task RequestsAClientCredentialsToken_WithTheConfiguredScope()
    {
        var identity = new StubIdentity(expiresIn: 3600);
        var provider = new ClientCredentialsTokenProvider(identity, Options, new ManualClock());

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
        var form = Assert.Single(identity.Forms);
        Assert.Contains("grant_type=client_credentials", form);
        Assert.Contains("client_id=expense-tracking-service", form);
        Assert.Contains("client_secret=s3cret", form);
        Assert.Contains("scope=events.read", form);
    }

    [Fact]
    public async Task ReusesTheToken_UntilItIsAboutToExpire()
    {
        var identity = new StubIdentity(expiresIn: 300);
        var clock = new ManualClock();
        var provider = new ClientCredentialsTokenProvider(identity, Options, clock);

        var first = await provider.GetTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(239)); // 300 - 60 de folga - 1
        var cached = await provider.GetTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        var renewed = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", cached);
        Assert.Equal("token-2", renewed);
        Assert.Equal(2, identity.Forms.Count);
    }

    [Fact]
    public async Task Rejection_ThrowsWithoutEchoingTheResponseBody()
    {
        var identity = new StubIdentity(expiresIn: 3600, status: HttpStatusCode.Unauthorized);
        var provider = new ClientCredentialsTokenProvider(identity, Options, new ManualClock());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetTokenAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("invalid_client", error.Message);
    }

    private sealed class StubIdentity(int expiresIn, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Forms { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Forms.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = status == HttpStatusCode.OK
                ? $$"""{"access_token":"token-{{Forms.Count}}","token_type":"Bearer","expires_in":{{expiresIn}}}"""
                : """{"error":"invalid_client"}""";
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
