using System.Security.Claims;
using Expense.Tracking.Reporting.Api.Setup;
using Expense.Tracking.Reporting.Application.Reminders;

namespace Expense.Tracking.Reporting.Api.Endpoints;

public sealed record DismissReminderRequest(string? Key);

/// <summary>NTF-8: o token do link de confirmacao do e-mail.</summary>
public sealed record ConfirmReminderEmailRequest(string? Token);

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

        // NTF-5 (ADR-0009, decisao 5): dispensar um aviso pela chave.
        app.MapPost("/reports/reminders/dismissals", async (
            DismissReminderRequest request, ClaimsPrincipal user, DismissReminderHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(ownerUserId, request.Key, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        })
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        // NTF-6 (ADR-0009, decisao 4): preferencias dos avisos (sem gravacao, os padroes).
        app.MapGet("/reports/reminder-settings", async (ClaimsPrincipal user, GetReminderSettingsHandler handler, CancellationToken ct) =>
            Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId)
                ? Results.Ok(await handler.HandleAsync(ownerUserId, ct))
                : Results.Unauthorized())
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.ReadPolicy);

        // NTF-8 (ADR-0009, decisao 6): endereco, liga/desliga e hora do resumo; confirmar e reenviar o link.
        app.MapPut("/reports/reminder-settings/email", async (
            ReminderEmailInput request, ClaimsPrincipal user, UpdateReminderEmailHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(ownerUserId, request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        app.MapPost("/reports/reminder-settings/email/confirm", async (
            ConfirmReminderEmailRequest request, ClaimsPrincipal user, ConfirmReminderEmailHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(ownerUserId, request.Token, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        app.MapPost("/reports/reminder-settings/email/resend", async (
            ClaimsPrincipal user, ResendReminderEmailConfirmationHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(ownerUserId, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        })
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        app.MapPut("/reports/reminder-settings", async (
            ReminderSettingsDto request, ClaimsPrincipal user, UpdateReminderSettingsHandler handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var ownerUserId))
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(ownerUserId, request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        })
        .WithTags("Reminders")
        .RequireAuthorization(AuthenticationSetup.WritePolicy);

        return app;
    }
}
