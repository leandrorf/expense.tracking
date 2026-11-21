using Expense.Tracking.Transactions.Domain.Imports;

namespace Expense.Tracking.Transactions.Application.Contracts;

/// <summary>IMP-4: lote de importacao com a previa das linhas. A chave externa nao sai na API.</summary>
public sealed record ImportBatchDto(
    Guid Id,
    string Status,
    string Format,
    string Kind,
    Guid AccountId,
    Guid? CreditCardId,
    string Currency,
    string DefaultPaymentMethod,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    ImportSummaryDto Summary,
    IReadOnlyList<ImportLineDto> Lines)
{
    public static ImportBatchDto From(ImportBatch batch) =>
        new(batch.Id, batch.Status.ToString(), batch.Format.ToString(), batch.Kind.ToString(), batch.AccountId,
            batch.CreditCardId, batch.Currency, batch.DefaultPaymentMethod.ToString(), batch.CreatedAtUtc,
            batch.ExpiresAtUtc, ImportSummaryDto.From(batch.Lines),
            batch.Lines.OrderBy(l => l.Number).Select(ImportLineDto.From).ToList());
}

public sealed record ImportSummaryDto(int Total, int New, int Duplicate, int Invalid, int ExcludedByDefault, int Included)
{
    public static ImportSummaryDto From(IReadOnlyCollection<ImportLine> lines) =>
        new(lines.Count,
            lines.Count(l => l.Status == ImportLineStatus.New),
            lines.Count(l => l.Status == ImportLineStatus.Duplicate),
            lines.Count(l => l.Status == ImportLineStatus.Invalid),
            lines.Count(l => l.Status == ImportLineStatus.ExcludedByDefault),
            lines.Count(l => l.Included));
}

public sealed record ImportLineDto(
    Guid Id,
    int Number,
    string Status,
    bool Included,
    DateOnly? OccurredOn,
    string? Type,
    decimal? Amount,
    string Description,
    string? PaymentMethod,
    string? Category,
    string? Problem)
{
    public static ImportLineDto From(ImportLine line) =>
        new(line.Id, line.Number, line.Status.ToString(), line.Included, line.OccurredOn, line.Type?.ToString(),
            line.Amount, line.Description, line.PaymentMethod?.ToString(), line.Category, line.Problem);
}
