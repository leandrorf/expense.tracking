using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Imports.Csv;

/// <summary>
/// IMP-11: mapeamento de colunas confirmado e guardado com um nome ("Banco X — fatura"), para as
/// proximas importacoes do mesmo banco nao precisarem remapear. Guarda tambem os nomes das colunas
/// do arquivo (so o cabecalho, nunca as linhas), para a SPA reconhecer o mesmo layout depois.
/// O nome e unico por usuario, sem diferenca de maiusculas: salvar de novo com o mesmo nome
/// substitui o mapeamento.
/// </summary>
public sealed class SavedCsvMapping
{
    public const int NameMaxLength = 60;
    public const int MaxPerUser = 50;
    public const int MaxHeaders = 50;
    public const int HeaderMaxLength = 100;

    private SavedCsvMapping(Guid id, Guid ownerUserId, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Mapping = null!;
        Headers = [];
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; }

    public CsvColumnMapping Mapping { get; private set; }

    /// <summary>Cabecalho do arquivo em que o mapeamento foi salvo; vazio quando o arquivo nao tinha.</summary>
    public IReadOnlyList<string> Headers { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<SavedCsvMapping> Create(
        Guid ownerUserId, string? name, CsvColumnMapping? mapping, IReadOnlyList<string>? headers, DateTimeOffset nowUtc)
    {
        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return Result.Failure<SavedCsvMapping>(validName.Error);
        }

        var saved = new SavedCsvMapping(Guid.NewGuid(), ownerUserId, validName.Value, nowUtc);
        var replaced = saved.Replace(mapping, headers, nowUtc);
        return replaced.IsFailure ? Result.Failure<SavedCsvMapping>(replaced.Error) : saved;
    }

    /// <summary>Salvar de novo com o mesmo nome: troca o mapeamento e o cabecalho.</summary>
    public Result Replace(CsvColumnMapping? mapping, IReadOnlyList<string>? headers, DateTimeOffset nowUtc)
    {
        if (mapping is null || !mapping.IsValid)
        {
            return Result.Failure(ImportErrors.MappingInvalid);
        }

        headers ??= [];
        if (headers.Count > MaxHeaders || headers.Any(h => h is null || h.Length > HeaderMaxLength))
        {
            return Result.Failure(ImportErrors.MappingHeadersInvalid);
        }

        Mapping = mapping;
        Headers = headers.Select(h => h.Trim()).ToList();
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public static Result<string> ValidateName(string? name)
    {
        var trimmed = ImportText.NormalizeSpaces(name);
        if (trimmed.Length == 0)
        {
            return Result.Failure<string>(ImportErrors.MappingNameRequired);
        }

        return trimmed.Length > NameMaxLength
            ? Result.Failure<string>(ImportErrors.MappingNameTooLong)
            : trimmed;
    }

    public bool HasName(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
}
