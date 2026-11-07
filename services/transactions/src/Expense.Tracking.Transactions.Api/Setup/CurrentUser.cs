using System.Security.Claims;

namespace Expense.Tracking.Transactions.Api.Setup;

public static class CurrentUser
{
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
