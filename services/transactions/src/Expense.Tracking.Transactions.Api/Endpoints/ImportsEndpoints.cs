using System.Text.Json;
using Expense.Tracking.Transactions.Api.Setup;
using Expense.Tracking.Transactions.Application.Imports;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;
using Expense.Tracking.Transactions.Domain.Transactions;
using Microsoft.AspNetCore.Http.Features;

namespace Expense.Tracking.Transactions.Api.Endpoints;

/// <summary>
/// IMP-4 (ADR-0004): envio do arquivo e previa da importacao. Upload em multipart lido a mao, com
/// limite de corpo por endpoint antes de ler qualquer byte, e sem nunca registrar o conteudo.
/// Campos: <c>file</c>, <c>accountId</c>, <c>creditCardId</c> (fatura), <c>paymentMethod</c>
/// (meio padrao do extrato) e <c>mapping</c> (JSON do mapeamento, so CSV).
/// </summary>
/// <summary>IMP-5: decisoes da previa. Linha omitida mantem o que a previa tinha.</summary>
public sealed record ConfirmImportRequest(IReadOnlyList<ConfirmImportLineRequest>? Lines);

public sealed record ConfirmImportLineRequest(Guid Id, bool? Included, string? Category, string? PaymentMethod);

public static class ImportsEndpoints
{
    /// <summary>Arquivo de ate 5 MB mais folga para os outros campos e as fronteiras do multipart.</summary>
    public const long MaxRequestBytes = ImportBatch.MaxFileBytes + 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapImportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/imports").WithTags("Imports");

        group.MapPost("/", async (HttpContext http, PreviewImportHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var form = await ReadUploadAsync(http, ct);
            if (form.IsFailure)
            {
                return HttpResults.Problem(form.Error);
            }

            var (fields, file) = form.Value;
            if (!Guid.TryParse(fields.GetValueOrDefault("accountId"), out var accountId))
            {
                return HttpResults.Problem(Error.Validation("import.account_required", "Informe a conta (accountId)."));
            }

            Guid? creditCardId = null;
            if (fields.GetValueOrDefault("creditCardId") is { Length: > 0 } cardText)
            {
                if (!Guid.TryParse(cardText, out var cardId))
                {
                    return HttpResults.Problem(Error.Validation("import.card_invalid", "creditCardId invalido."));
                }

                creditCardId = cardId;
            }

            PaymentMethod? paymentMethod = null;
            if (fields.GetValueOrDefault("paymentMethod") is { Length: > 0 } methodText)
            {
                if (!Enum.TryParse<PaymentMethod>(methodText, ignoreCase: true, out var method) || !Enum.IsDefined(method))
                {
                    return HttpResults.Problem(Error.Validation("transaction.payment_method_invalid", "Meio de pagamento invalido."));
                }

                paymentMethod = method;
            }

            CsvColumnMapping? mapping = null;
            if (fields.GetValueOrDefault("mapping") is { Length: > 0 } mappingJson)
            {
                try
                {
                    mapping = JsonSerializer.Deserialize<CsvColumnMapping>(mappingJson, JsonOptions);
                }
                catch (JsonException)
                {
                    return HttpResults.Problem(ImportErrors.MappingInvalid);
                }
            }

            var result = await handler.HandleAsync(
                new PreviewImportCommand(owner.Value, accountId, creditCardId, paymentMethod, file, mapping), ct);

            return result.IsSuccess
                ? Results.Created($"/imports/{result.Value.Id}", result.Value)
                : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/csv/proposal", async (HttpContext http, ProposeCsvMappingHandler handler, CancellationToken ct) =>
        {
            if (http.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            var form = await ReadUploadAsync(http, ct);
            if (form.IsFailure)
            {
                return HttpResults.Problem(form.Error);
            }

            var result = handler.Handle(form.Value.File);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapPost("/{id:guid}/confirm", async (
            Guid id, ConfirmImportRequest? request, HttpContext http, ConfirmImportHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var decisions = new List<ImportLineDecision>();
            foreach (var line in request?.Lines ?? [])
            {
                PaymentMethod? method = null;
                if (line.PaymentMethod is { Length: > 0 } methodText)
                {
                    if (!Enum.TryParse<PaymentMethod>(methodText, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    {
                        return HttpResults.Problem(Error.Validation("transaction.payment_method_invalid", "Meio de pagamento invalido."));
                    }

                    method = parsed;
                }

                decisions.Add(new ImportLineDecision(line.Id, line.Included, line.Category, method));
            }

            var result = await handler.HandleAsync(new ConfirmImportCommand(owner.Value, id, decisions), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.WritePolicy);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, GetImportHandler handler, CancellationToken ct) =>
        {
            var owner = http.User.GetUserId();
            if (owner is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(id, owner.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : HttpResults.Problem(result.Error);
        }).RequireAuthorization(AuthenticationSetup.ReadPolicy);

        return app;
    }

    /// <summary>
    /// Le o multipart com limite de tamanho. Erros do framework ao ler o corpo viram codigos
    /// estaveis; a mensagem original nao e repassada.
    /// </summary>
    private static async Task<Result<(Dictionary<string, string> Fields, UploadedFile File)>> ReadUploadAsync(
        HttpContext http, CancellationToken ct)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            sizeFeature.MaxRequestBodySize = MaxRequestBytes;
        }

        if (http.Request.ContentLength > MaxRequestBytes)
        {
            return Result.Failure<(Dictionary<string, string>, UploadedFile)>(ImportErrors.FileTooLarge);
        }

        if (!http.Request.HasFormContentType)
        {
            return Result.Failure<(Dictionary<string, string>, UploadedFile)>(ImportErrors.FileRequired);
        }

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = MaxRequestBytes,
                ValueCountLimit = 16,
                ValueLengthLimit = 16 * 1024,
            }, ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Result.Failure<(Dictionary<string, string>, UploadedFile)>(ImportErrors.FileTooLarge);
        }
        catch (InvalidDataException)
        {
            // Limite de multipart estourado ou corpo malformado: o framework nao distingue bem.
            return Result.Failure<(Dictionary<string, string>, UploadedFile)>(
                ImportErrors.FormatInvalid("Envio invalido: use multipart/form-data com o arquivo em 'file' (ate 5 MB)."));
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Result.Failure<(Dictionary<string, string>, UploadedFile)>(ImportErrors.FileRequired);
        }

        if (file.Length > ImportBatch.MaxFileBytes)
        {
            return Result.Failure<(Dictionary<string, string>, UploadedFile)>(ImportErrors.FileTooLarge);
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var fields = form.ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return (fields, new UploadedFile(Path.GetFileName(file.FileName), buffer.ToArray()));
    }
}
