using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Reminders;

namespace Expense.Tracking.Reporting.Api.Endpoints;

public static class RemindersEndpoints
{
    public static IEndpointRouteBuilder MapRemindersEndpoints(this IEndpointRouteBuilder app)
    {
        // NTF-2 (ADR-0009, decisoes 2 e 3): o que vence, calculado na hora; pela rota /api/reports/** do gateway.
        app.MapGet("/reports/reminders", async (ClaimsPrincipal user, GetRemindersHandler handler, CancellationToken ct) =>
            Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId)
                ? Results.Ok(await handler.HandleAsync(ownerUserId, ct))
                : Results.Unauthorized())
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
