using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Imports.Csv;

namespace Expense.Tracking.Transactions.Application.Imports;

/// <summary>IMP-11: mapeamento salvo, como a API devolve.</summary>
public sealed record SavedCsvMappingDto(
    Guid Id, string Name, CsvColumnMapping Mapping, IReadOnlyList<string> Headers, DateTimeOffset UpdatedAtUtc)
{
    public static SavedCsvMappingDto From(SavedCsvMapping saved) =>
        new(saved.Id, saved.Name, saved.Mapping, saved.Headers, saved.UpdatedAtUtc);
}

public sealed record SaveCsvMappingCommand(
    Guid OwnerUserId, string? Name, CsvColumnMapping? Mapping, IReadOnlyList<string>? Headers);

/// <summary>Salvo pela primeira vez (201) ou substituido pelo mesmo nome (200).</summary>
public sealed record SaveCsvMappingResult(SavedCsvMappingDto Mapping, bool Created);

public sealed class ListCsvMappingsHandler(ICsvMappingRepository mappings)
{
    public async Task<IReadOnlyList<SavedCsvMappingDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        (await mappings.ListAsync(ownerUserId, cancellationToken)).Select(SavedCsvMappingDto.From).ToList();
}

/// <summary>
/// IMP-11: salva o mapeamento com um nome. O mesmo nome (sem diferenca de maiusculas) substitui o
/// salvo antes. Dois salvamentos do mesmo nome novo em paralelo: o indice unico barra o segundo,
/// que recarrega e substitui o que o primeiro gravou.
/// </summary>
public sealed class SaveCsvMappingHandler(
    ICsvMappingRepository mappings,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private const int MaxAttempts = 2;

    public async Task<Result<SaveCsvMappingResult>> HandleAsync(SaveCsvMappingCommand command, CancellationToken cancellationToken)
    {
        var name = SavedCsvMapping.ValidateName(command.Name);
        if (name.IsFailure)
        {
            return Result.Failure<SaveCsvMappingResult>(name.Error);
        }

        for (var attempt = 1; ; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var saved = await mappings.ListAsync(command.OwnerUserId, cancellationToken);
            var existing = saved.FirstOrDefault(m => m.HasName(name.Value));

            SavedCsvMapping result;
            if (existing is not null)
            {
                var replaced = existing.Replace(command.Mapping, command.Headers, now);
                if (replaced.IsFailure)
                {
                    return Result.Failure<SaveCsvMappingResult>(replaced.Error);
                }

                result = existing;
            }
            else
            {
                if (saved.Count >= SavedCsvMapping.MaxPerUser)
                {
                    return Result.Failure<SaveCsvMappingResult>(ImportErrors.MappingsLimit);
                }

                var created = SavedCsvMapping.Create(command.OwnerUserId, name.Value, command.Mapping, command.Headers, now);
                if (created.IsFailure)
                {
                    return Result.Failure<SaveCsvMappingResult>(created.Error);
                }

                result = created.Value;
                await mappings.AddAsync(result, cancellationToken);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (PersistenceConflictException) when (attempt < MaxAttempts)
            {
                unitOfWork.Reset();
                continue;
            }

            return new SaveCsvMappingResult(SavedCsvMappingDto.From(result), existing is null);
        }
    }
}

public sealed class DeleteCsvMappingHandler(ICsvMappingRepository mappings, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var saved = (await mappings.ListAsync(ownerUserId, cancellationToken)).FirstOrDefault(m => m.Id == id);
        if (saved is null)
        {
            return Result.Failure(ImportErrors.MappingNotFound);
        }

        mappings.Remove(saved);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
