using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Application.OAuth;

/// <summary>
/// Autentica um cliente OAuth no endpoint de token. Clientes confidenciais exigem
/// client_secret valido; clientes publicos (SPA) nao enviam segredo.
/// </summary>
public static class ClientAuthentication
{
    public static Result Authenticate(OAuthClient client, string? providedSecret, IPasswordHasher passwordHasher)
    {
        if (!client.IsActive)
        {
            return Result.Failure(OAuthErrors.InvalidClient("Cliente inativo."));
        }

        if (!client.IsConfidential)
        {
            // Cliente publico: nao deve apresentar segredo.
            return string.IsNullOrEmpty(providedSecret)
                ? Result.Success()
                : Result.Failure(OAuthErrors.InvalidClient("Cliente publico nao deve enviar client_secret."));
        }

        // Cliente confidencial: segredo obrigatorio e valido.
        if (string.IsNullOrEmpty(providedSecret) || string.IsNullOrEmpty(client.ClientSecretHash))
        {
            return Result.Failure(OAuthErrors.InvalidClient("Credenciais de cliente invalidas."));
        }

        return passwordHasher.Verify(providedSecret, client.ClientSecretHash)
            ? Result.Success()
            : Result.Failure(OAuthErrors.InvalidClient("Credenciais de cliente invalidas."));
    }
}
