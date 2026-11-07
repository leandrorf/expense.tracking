using Expense.Tracking.Accounts.Api.Setup;
using Expense.Tracking.Accounts.Domain.Payments;

namespace Expense.Tracking.Accounts.Api.Endpoints;

public static class PaymentMethodsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentMethodsEndpoints(this IEndpointRouteBuilder app)
    {
        // Catalogo estavel de meios de pagamento (dinheiro, Pix, boleto, cartoes, transferencia).
        app.MapGet("/payment-methods", () => Results.Ok(
            PaymentMethodCatalog.All.Select(m => new
            {
                code = m.Code,
                name = m.DisplayName,
                requiresCard = m.RequiresCard
            })))
            .WithTags("PaymentMethods")
            .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }
}
