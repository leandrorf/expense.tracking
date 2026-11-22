using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Domain.Forecast;

namespace Expense.Tracking.Reporting.Api.Endpoints;

public static class ForecastEndpoints
{
    public static IEndpointRouteBuilder MapForecastEndpoints(this IEndpointRouteBuilder app)
    {
        // INV-8 (ADR-0005, decisoes 8 e 11): previsao de caixa do mes atual ate `months` meses (1 a 12).
        app.MapGet("/reports/cash-forecast", async (
            int? months, ClaimsPrincipal user, GetCashForecastHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(ownerUserId, months ?? CashForecastCalculator.DefaultMonths, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .WithTags("Reports")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
