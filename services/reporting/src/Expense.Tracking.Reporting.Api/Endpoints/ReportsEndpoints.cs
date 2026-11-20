using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Reports;

namespace Expense.Tracking.Reporting.Api.Endpoints;

public static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports");

        // EVT-3: le a projecao local, filtrada pelo dono (sub do token). Responde mesmo com o
        // transactions ou o accounts fora do ar, possivelmente com dados defasados.
        group.MapGet("/summary", async (
            GetSummaryHandler handler,
            ClaimsPrincipal user,
            CancellationToken ct,
            Guid? accountId,
            DateOnly? from,
            DateOnly? to) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var summary = await handler.HandleAsync(ownerUserId, new ReportFilter(accountId, from, to), ct);
            return Results.Ok(summary);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
