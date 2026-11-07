using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Domain.Clients;

/// <summary>
/// Cliente registrado no Identity Server (relying party). Define como ele pode obter tokens.
/// </summary>
public sealed class OAuthClient
{
    private readonly List<string> _redirectUris = [];
    private readonly List<string> _allowedScopes = [];
    private readonly List<string> _allowedGrantTypes = [];

    // Construtor usado pelo EF Core na materializacao.
    private OAuthClient(Guid id, string clientId, string displayName, ClientType type, string? clientSecretHash, bool requirePkce, bool isActive, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ClientId = clientId;
        DisplayName = displayName;
        Type = type;
        ClientSecretHash = clientSecretHash;
        RequirePkce = requirePkce;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string ClientId { get; private set; }

    public string DisplayName { get; private set; }

    public ClientType Type { get; private set; }

    /// <summary>Hash do client_secret (apenas para clientes confidenciais).</summary>
    public string? ClientSecretHash { get; private set; }

    public bool RequirePkce { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<string> RedirectUris => _redirectUris.AsReadOnly();

    public IReadOnlyCollection<string> AllowedScopes => _allowedScopes.AsReadOnly();

    public IReadOnlyCollection<string> AllowedGrantTypes => _allowedGrantTypes.AsReadOnly();

    public bool IsConfidential => Type == ClientType.Confidential;

    public static OAuthClient Create(
        string clientId,
        string displayName,
        ClientType type,
        string? clientSecretHash,
        IEnumerable<string> redirectUris,
        IEnumerable<string> allowedScopes,
        IEnumerable<string> allowedGrantTypes,
        DateTimeOffset nowUtc,
        bool requirePkce = true)
    {
        var client = new OAuthClient(
            id: Guid.NewGuid(),
            clientId: clientId,
            displayName: displayName,
            type: type,
            clientSecretHash: clientSecretHash,
            requirePkce: type == ClientType.Public || requirePkce,
            isActive: true,
            createdAtUtc: nowUtc);

        client._redirectUris.AddRange(redirectUris.Distinct(StringComparer.Ordinal));
        client._allowedScopes.AddRange(allowedScopes.Distinct(StringComparer.Ordinal));
        client._allowedGrantTypes.AddRange(allowedGrantTypes.Where(GrantType.IsSupported).Distinct(StringComparer.Ordinal));

        return client;
    }

    public bool HasRedirectUri(string redirectUri) =>
        _redirectUris.Contains(redirectUri, StringComparer.Ordinal);

    public bool AllowsGrant(string grantType) =>
        _allowedGrantTypes.Contains(grantType, StringComparer.Ordinal);

    /// <summary>Filtra os escopos solicitados para apenas os permitidos ao cliente.</summary>
    public IReadOnlyCollection<string> FilterAllowedScopes(IEnumerable<string> requestedScopes) =>
        requestedScopes.Where(s => _allowedScopes.Contains(s, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();

    public Result EnsureScopesAllowed(IEnumerable<string> requestedScopes)
    {
        var invalid = requestedScopes.Where(s => !_allowedScopes.Contains(s, StringComparer.Ordinal)).ToArray();
        return invalid.Length > 0
            ? Result.Failure(Error.Validation("scope.invalid", $"Escopo(s) nao permitido(s) para o cliente: {string.Join(' ', invalid)}."))
            : Result.Success();
    }
}
