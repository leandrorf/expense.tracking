using Expense.Tracking.Accounts.Api.Setup;
using Expense.Tracking.Accounts.Application.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace Expense.Tracking.Accounts.Api.Endpoints;

/// <summary>
/// Endpoints internos, para outros servicos (token de servico, client credentials). O gateway
/// bloqueia <c>/api/internal/**</c>, entao eles so sao alcancaveis dentro da rede dos servicos.
/// </summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").WithTags("Internal");

        // EVT-4: escopo PF/PJ de contas de qualquer dono, sem outros dados da conta.
        // GET /internal/account-scopes?ids={id}&ids={id}... (ate 100 ids)
        group.MapGet("/account-scopes", async ([FromQuery] Guid[] ids, GetAccountScopesHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ids, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.AccountScopesReadPolicy);

        return app;
    }
}
