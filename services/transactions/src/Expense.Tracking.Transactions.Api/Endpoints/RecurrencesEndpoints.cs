using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Recurrences;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Recurrences;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Api.Endpoints;

/// <summary>
/// REC-2: corpo da regra (criar e editar). Os mesmos campos do lancamento, sem parcelas, mais frequencia,
/// inicio (ancora da agenda) e fim opcional. Na edicao, a conta so e conferida e a versao e obrigatoria.
/// </summary>
public sealed record RecurrenceRequest(
    Guid AccountId,
    string? Type,
    string? PaymentMethod,
    Guid? CreditCardId,
    string? Description,
    string? Category,
    decimal Amount,
    string? Currency,
    string? Notes,
    string? Frequency,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int? Version = null,
    Guid? FromTransactionId = null);

/// <summary>REC-3: lancar um periodo; sem corpo (ou campos nulos) valem o valor, a data agendada e as observacoes da regra.</summary>
public sealed record LaunchOccurrenceRequest(decimal? Amount, DateOnly? OccurredOn, string? Notes);

public static class RecurrencesEndpoints
{
    public static IEndpointRouteBuilder MapRecurrencesEndpoints(this IEndpointRouteBuilder app)
    {
        // Pela rota existente do gateway (/api/transactions/**); nenhum escopo novo (ADR-0008, decisao 12).
        var group = app.MapGroup("/transactions/recurrences").WithTags("Recurrences");

        group.MapGet("/", async (HttpContext http, ListRecurrencesHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            return owner is null ? Results.Unauthorized() : Results.Ok(await handler.HandleAsync(owner.Value, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetRecurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/", async (RecurrenceRequest request, HttpContext http, CreateRecurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var fields = ParseFields(request);
            if (fields.IsFailure)
            {
                return HttpResults.Problem(fields.Error);
            }

            var result = await handler.HandleAsync(
                new CreateRecurrenceCommand(owner.Value, request.AccountId, fields.Value, request.FromTransactionId), ct);
            return result.IsSuccess
                ? Results.Created($"/transactions/recurrences/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPut("/{id:guid}", async (
            Guid id, RecurrenceRequest request, HttpContext http, UpdateRecurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var fields = ParseFields(request);
            if (fields.IsFailure)
            {
                return HttpResults.Problem(fields.Error);
            }

            if (request.Version is not { } version)
            {
                return HttpResults.Problem(Error.Validation("recurrence.version_required", "Informe a versao da recorrencia que voce leu."));
            }

            var result = await handler.HandleAsync(
                new UpdateRecurrenceCommand(id, owner.Value, request.AccountId, fields.Value, version), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, DeleteRecurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // REC-3 (ADR-0008, decisoes 7 e 8): "a lancar", lancar, pular e desfazer o pulo de um periodo.
        group.MapGet("/due", async (HttpContext http, ListDueOccurrencesHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            return owner is null ? Results.Unauthorized() : Results.Ok(await handler.HandleAsync(owner.Value, ct));
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        group.MapPost("/{id:guid}/occurrences/{period}/launch", async (
            Guid id, string period, LaunchOccurrenceRequest? request, HttpContext http, LaunchOccurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(
                new LaunchOccurrenceCommand(id, owner.Value, period, request?.Amount, request?.OccurredOn, request?.Notes), ct);
            return result.IsSuccess
                ? Results.Created($"/transactions/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/{id:guid}/occurrences/{period}/skip", async (
            Guid id, string period, HttpContext http, SkipOccurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, period, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}/occurrences/{period}/skip", async (
            Guid id, string period, HttpContext http, UnskipOccurrenceHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, period, ct);
            return result.IsSuccess ? Results.NoContent() : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        return app;
    }

    private static Result<RecurrenceFields> ParseFields(RecurrenceRequest request)
    {
        var type = TransactionType.Expense;
        if (!string.IsNullOrWhiteSpace(request.Type) && !Enum.TryParse(request.Type, ignoreCase: true, out type))
        {
            return Result.Failure<RecurrenceFields>(Error.Validation("transaction.type_invalid", "Tipo invalido: use 'Expense' ou 'Income'."));
        }

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var paymentMethod)
            || !Enum.IsDefined(paymentMethod))
        {
            return Result.Failure<RecurrenceFields>(Error.Validation("transaction.payment_method_invalid", "Meio de pagamento invalido."));
        }

        var frequency = RecurrenceFrequency.Monthly;
        if (!string.IsNullOrWhiteSpace(request.Frequency)
            && (!Enum.TryParse(request.Frequency, ignoreCase: true, out frequency) || !Enum.IsDefined(frequency)))
        {
            return Result.Failure<RecurrenceFields>(Error.Validation("recurrence.frequency_invalid", "Frequencia invalida: use 'Monthly' ou 'Yearly'."));
        }

        if (request.StartsOn is not { } startsOn)
        {
            return Result.Failure<RecurrenceFields>(Error.Validation("recurrence.starts_on_required", "Informe a data de inicio da recorrencia."));
        }

        return new RecurrenceFields(type, paymentMethod, request.CreditCardId, request.Description, request.Category,
            request.Amount, request.Currency, request.Notes, frequency, startsOn, request.EndsOn);
    }
}
