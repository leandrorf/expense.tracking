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
    Guid? FromTransactionId = null,
    RecurrenceAdjustmentRequest? Adjustment = null);

/// <summary>
/// RJT-4 (ADR-0010, decisao 3): reajuste anual. <c>kind</c> = <c>Index</c> (com <c>index</c> IPCA, INPC ou
/// IGPM) ou <c>Fixed</c> (com <c>rate</c> em %); mes padrao = o do inicio; "nao reduzir" padrao ligado.
/// </summary>
public sealed record RecurrenceAdjustmentRequest(int? AnniversaryMonth, string? Kind, string? Index, decimal? Rate, bool? NoReduction);

/// <summary>RJT-4 (ADR-0010, decisao 4): aplicar o reajuste de um ano pelo percentual ou pelo valor novo.</summary>
public sealed record ApplyAdjustmentRequest(int? Year, decimal? Rate, decimal? Amount, int? Version);

/// <summary>RJT-2: valor a partir de um periodo, com a versao lida da regra.</summary>
public sealed record RecurrenceAmountRequest(string? FromPeriod, decimal Amount, int? Version);

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

        // RJT-2 (ADR-0010, decisoes 1 e 2): valor com vigencia.
        group.MapPost("/{id:guid}/amounts", async (
            Guid id, RecurrenceAmountRequest request, HttpContext http, SetRecurrenceAmountHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (request.Version is not { } version)
            {
                return HttpResults.Problem(Error.Validation("recurrence.version_required", "Informe a versao da recorrencia que voce leu."));
            }

            var result = await handler.HandleAsync(
                new SetRecurrenceAmountCommand(id, owner.Value, request.FromPeriod ?? string.Empty, request.Amount, version), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapDelete("/{id:guid}/amounts/{fromPeriod}", async (
            Guid id, string fromPeriod, int? version, HttpContext http, RemoveRecurrenceAmountHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (version is not { } v)
            {
                return HttpResults.Problem(Error.Validation("recurrence.version_required", "Informe a versao da recorrencia que voce leu."));
            }

            var result = await handler.HandleAsync(id, owner.Value, fromPeriod, v, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        // RJT-4 (ADR-0010, decisao 4): confirma o reajuste de um aniversario.
        group.MapPost("/{id:guid}/adjustments", async (
            Guid id, ApplyAdjustmentRequest request, HttpContext http, ApplyRecurrenceAdjustmentHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            if (request.Version is not { } version)
            {
                return HttpResults.Problem(Error.Validation("recurrence.version_required", "Informe a versao da recorrencia que voce leu."));
            }

            if (request.Year is not { } year)
            {
                return HttpResults.Problem(Error.Validation("recurrence.adjustment_year_invalid", "Informe o ano do reajuste."));
            }

            var result = await handler.HandleAsync(
                new ApplyRecurrenceAdjustmentCommand(id, owner.Value, year, request.Rate, request.Amount, version), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
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

        var adjustment = ParseAdjustment(request.Adjustment);
        if (adjustment.IsFailure)
        {
            return Result.Failure<RecurrenceFields>(adjustment.Error);
        }

        return new RecurrenceFields(type, paymentMethod, request.CreditCardId, request.Description, request.Category,
            request.Amount, request.Currency, request.Notes, frequency, startsOn, request.EndsOn, adjustment.Value);
    }

    private static Result<RecurrenceAdjustmentSpec?> ParseAdjustment(RecurrenceAdjustmentRequest? request)
    {
        if (request is null)
        {
            return Result.Success<RecurrenceAdjustmentSpec?>(null);
        }

        if (!Enum.TryParse<RecurrenceAdjustmentKind>(request.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind)
            || int.TryParse(request.Kind, out _))
        {
            return Result.Failure<RecurrenceAdjustmentSpec?>(Error.Validation(
                "recurrence.adjustment_kind_invalid", "Tipo de reajuste invalido: use 'Index' ou 'Fixed'."));
        }

        EconomicIndex? index = null;
        if (kind == RecurrenceAdjustmentKind.Index)
        {
            if (!Enum.TryParse<EconomicIndex>(request.Index, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)
                || int.TryParse(request.Index, out _))
            {
                return Result.Failure<RecurrenceAdjustmentSpec?>(Error.Validation(
                    "recurrence.adjustment_index_invalid", "Informe o indice do reajuste: IPCA, INPC ou IGPM."));
            }

            index = parsed;
        }

        return new RecurrenceAdjustmentSpec(request.AnniversaryMonth, kind, index, request.Rate, request.NoReduction ?? true);
    }
}
