using Expense.Tracking.Identity.Domain.Clients;

namespace Expense.Tracking.Identity.UnitTests.Domain;

public sealed class OAuthClientTests
{
    private static OAuthClient CreateSpa() => OAuthClient.Create(
        clientId: "spa",
        displayName: "SPA",
        type: ClientType.Public,
        clientSecretHash: null,
        redirectUris: ["https://app/callback"],
        allowedScopes: ["openid", "expenses.read"],
        allowedGrantTypes: [GrantType.AuthorizationCode, GrantType.RefreshToken],
        nowUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void PublicClient_AlwaysRequiresPkce()
    {
        var client = OAuthClient.Create(
            "spa", "SPA", ClientType.Public, null,
            ["https://app/callback"], ["openid"], [GrantType.AuthorizationCode],
            DateTimeOffset.UtcNow, requirePkce: false);

        Assert.True(client.RequirePkce);
    }

    [Fact]
    public void HasRedirectUri_MatchesExactly()
    {
        var client = CreateSpa();

        Assert.True(client.HasRedirectUri("https://app/callback"));
        Assert.False(client.HasRedirectUri("https://app/callback/"));
        Assert.False(client.HasRedirectUri("https://evil/callback"));
    }

    [Fact]
    public void AllowsGrant_OnlyRegisteredGrants()
    {
        var client = CreateSpa();

        Assert.True(client.AllowsGrant(GrantType.AuthorizationCode));
        Assert.False(client.AllowsGrant(GrantType.ClientCredentials));
    }

    [Fact]
    public void EnsureScopesAllowed_RejectsUnknownScope()
    {
        var client = CreateSpa();

        Assert.True(client.EnsureScopesAllowed(["openid"]).IsSuccess);
        Assert.True(client.EnsureScopesAllowed(["admin"]).IsFailure);
    }

    [Fact]
    public void Create_IgnoresUnsupportedGrantTypes()
    {
        var client = OAuthClient.Create(
            "c", "C", ClientType.Confidential, "hash",
            [], ["s"], [GrantType.ClientCredentials, "password"],
            DateTimeOffset.UtcNow);

        Assert.Contains(GrantType.ClientCredentials, client.AllowedGrantTypes);
        Assert.DoesNotContain("password", client.AllowedGrantTypes);
    }
}
