using Expense.Tracking.Identity.Domain.Clients;

namespace Expense.Tracking.Identity.Api.Setup;

/// <summary>
/// IDX-2 (ADR-0003): clientes OAuth e usuarios que o seed cria. Vem toda da configuracao de quem
/// roda o Identity; sem configuracao, nenhum cliente nem usuario e criado.
/// </summary>
/// <remarks>
/// Os dicionarios sao indexados pelo <c>client_id</c> e por um nome logico do usuario, para que um
/// segredo possa ser sobrescrito por variavel de ambiente sem depender de posicao em lista
/// (ex.: <c>Seed__Clients__expense-tracking-service__Secret</c>).
/// </remarks>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }

    public Dictionary<string, SeedClientOptions> Clients { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, SeedUserOptions> Users { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Erros de configuracao, um por linha, apontando a chave. Nunca inclui o valor de segredos ou
    /// senhas.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        foreach (var (clientId, client) in Clients)
        {
            var key = $"{SectionName}:Clients:{clientId}";
            if (string.IsNullOrWhiteSpace(clientId))
            {
                errors.Add($"{SectionName}:Clients: client_id vazio.");
                continue;
            }

            if (client.GrantTypes.Length == 0)
            {
                errors.Add($"{key}:GrantTypes: informe ao menos um grant.");
            }

            foreach (var grant in client.GrantTypes.Where(g => !GrantType.IsSupported(g)))
            {
                errors.Add($"{key}:GrantTypes: grant '{grant}' nao suportado (use {string.Join(", ", GrantType.All)}).");
            }

            if (client.GrantTypes.Contains(GrantType.AuthorizationCode, StringComparer.Ordinal) && client.RedirectUris.Length == 0)
            {
                errors.Add($"{key}:RedirectUris: authorization_code exige ao menos um redirect URI.");
            }

            if (client.Type == ClientType.Confidential && string.IsNullOrWhiteSpace(client.Secret))
            {
                errors.Add($"{key}:Secret: cliente confidencial exige segredo.");
            }

            if (client.Type == ClientType.Public && !string.IsNullOrEmpty(client.Secret))
            {
                errors.Add($"{key}:Secret: cliente publico nao pode ter segredo.");
            }
        }

        foreach (var (name, user) in Users)
        {
            var key = $"{SectionName}:Users:{name}";
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                errors.Add($"{key}:Email: obrigatorio.");
            }

            if (string.IsNullOrEmpty(user.Password))
            {
                errors.Add($"{key}:Password: obrigatoria.");
            }
        }

        return errors;
    }
}

public sealed class SeedClientOptions
{
    public string? DisplayName { get; set; }

    public ClientType Type { get; set; } = ClientType.Public;

    /// <summary>Segredo em texto (so para cliente confidencial); gravado como hash.</summary>
    public string? Secret { get; set; }

    public string[] RedirectUris { get; set; } = [];

    public string[] Scopes { get; set; } = [];

    /// <summary>Nomes do OAuth2: <c>authorization_code</c>, <c>refresh_token</c>, <c>client_credentials</c>.</summary>
    public string[] GrantTypes { get; set; } = [];

    /// <summary>Ignorado para cliente publico, que sempre exige PKCE.</summary>
    public bool RequirePkce { get; set; } = true;
}

public sealed class SeedUserOptions
{
    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? DisplayName { get; set; }
}
