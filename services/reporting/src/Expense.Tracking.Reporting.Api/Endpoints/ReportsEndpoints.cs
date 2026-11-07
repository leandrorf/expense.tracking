using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Reports;

namespace Expense.Tracking.Reporting.Api.Endpoints;

public static class ReportsEndpoints
{
    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/summary", async (
            GetSummaryHandler handler,
            CancellationToken ct,
            Guid? accountId,
            DateOnly? from,
            DateOnly? to) =>
        {
            var summary = await handler.HandleAsync(new ReportFilter(accountId, from, to), ct);
            return Results.Ok(summary);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
