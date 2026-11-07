using System.Security.Claims;

namespace Expense.Tracking.Accounts.Api.Setup;

public static class CurrentUser
{
    /// <summary>Extrai o id do usuario (claim 'sub') do token. Retorna null se ausente/invalido.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
