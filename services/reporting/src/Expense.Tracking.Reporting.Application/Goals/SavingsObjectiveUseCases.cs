using System.Globalization;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Goals;

namespace Expense.Tracking.Reporting.Application.Goals;

/// <param name="Date">Data do aporte, <c>yyyy-MM-dd</c>.</param>
/// <param name="Amount">Valor guardado; negativo = retirada.</param>
public sealed record SavingsContributionDto(Guid Id, string Date, decimal Amount, string? Note);

/// <param name="Scope"><c>personal</c> ou <c>business</c>.</param>
/// <param name="Deadline">Mes do prazo, <c>yyyy-MM</c>; nulo = sem prazo.</param>
/// <param name="Saved">Soma dos aportes.</param>
/// <param name="Contributions">Aportes do mais novo ao mais antigo.</param>
public sealed record SavingsObjectiveDto(
    Guid Id, string Scope, string Name, decimal Target, string? Deadline, bool Archived, decimal Saved,
    IReadOnlyList<SavingsContributionDto> Contributions)
{
    public static SavingsObjectiveDto From(SavingsObjective objective) => new(
        objective.Id,
        GoalScopes.ToRoute(objective.Scope),
        objective.Name,
        objective.Target,
        objective.Deadline?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        objective.IsArchived,
        objective.Saved,
        objective.Contributions
            .Reverse()
            .Select(c => new SavingsContributionDto(c.Id, c.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.Amount, c.Note))
            .ToList());
}

/// <summary>MET-5: objetivos de economia do usuario no banco do reporting (ADR-0013).</summary>
public interface ISavingsObjectiveRepository
{
    Task<IReadOnlyList<SavingsObjective>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>O objetivo do dono, para alterar; nulo se nao existe ou e de outro usuario.</summary>
    Task<SavingsObjective?> FindAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);

    Task AddAsync(SavingsObjective objective, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record CreateSavingsObjectiveCommand(Guid OwnerUserId, GoalScope Scope, string? Name, decimal Target, DateOnly? Deadline);

public sealed record EditSavingsObjectiveCommand(Guid Id, Guid OwnerUserId, string? Name, decimal Target, DateOnly? Deadline);

public sealed record AddSavingsContributionCommand(Guid Id, Guid OwnerUserId, DateOnly Date, decimal Amount, string? Note);

/// <summary>
/// MET-5 (ADR-0013, regra 6): criar, editar, arquivar e reabrir objetivos, e registrar ou excluir aportes. "Hoje"
/// no fuso do app (o mesmo das faturas). Objetivo alheio = 404.
/// </summary>
public sealed class SavingsObjectiveHandler(ISavingsObjectiveRepository objectives, InvoiceOptions options, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<SavingsObjectiveDto>> ListAsync(Guid ownerUserId, bool includeArchived, CancellationToken cancellationToken) =>
        (await objectives.ListAsync(ownerUserId, cancellationToken))
            .Where(o => includeArchived || !o.IsArchived)
            .OrderBy(o => o.IsArchived)
            .ThenBy(o => o.Scope)
            .ThenBy(o => o.CreatedAt)
            .Select(SavingsObjectiveDto.From)
            .ToList();

    public async Task<Result<SavingsObjectiveDto>> CreateAsync(CreateSavingsObjectiveCommand command, CancellationToken cancellationToken)
    {
        var created = SavingsObjective.Create(
            command.OwnerUserId, command.Scope, command.Name, command.Target, command.Deadline, Today(), timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<SavingsObjectiveDto>(created.Error);
        }

        await objectives.AddAsync(created.Value, cancellationToken);
        return SavingsObjectiveDto.From(created.Value);
    }

    public Task<Result<SavingsObjectiveDto>> EditAsync(EditSavingsObjectiveCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.Id, command.OwnerUserId, o => o.Edit(command.Name, command.Target, command.Deadline, Today()), cancellationToken);

    public Task<Result<SavingsObjectiveDto>> ArchiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        ApplyAsync(id, ownerUserId, o =>
        {
            o.Archive(timeProvider.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<SavingsObjectiveDto>> UnarchiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) =>
        ApplyAsync(id, ownerUserId, o =>
        {
            o.Unarchive();
            return Result.Success();
        }, cancellationToken);

    public Task<Result<SavingsObjectiveDto>> AddContributionAsync(AddSavingsContributionCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.Id, command.OwnerUserId, o =>
        {
            var added = o.AddContribution(command.Date, command.Amount, command.Note, Today(), timeProvider.GetUtcNow());
            return added.IsSuccess ? Result.Success() : Result.Failure(added.Error);
        }, cancellationToken);

    public Task<Result<SavingsObjectiveDto>> RemoveContributionAsync(
        Guid id, Guid ownerUserId, Guid contributionId, CancellationToken cancellationToken) =>
        ApplyAsync(id, ownerUserId, o => o.RemoveContribution(contributionId), cancellationToken);

    private DateOnly Today() => CardInvoiceSource.Today(timeProvider, options);

    private async Task<Result<SavingsObjectiveDto>> ApplyAsync(
        Guid id, Guid ownerUserId, Func<SavingsObjective, Result> change, CancellationToken cancellationToken)
    {
        var objective = await objectives.FindAsync(id, ownerUserId, cancellationToken);
        if (objective is null)
        {
            return Result.Failure<SavingsObjectiveDto>(GoalErrors.ObjectiveNotFound);
        }

        var result = change(objective);
        if (result.IsFailure)
        {
            return Result.Failure<SavingsObjectiveDto>(result.Error);
        }

        await objectives.SaveChangesAsync(cancellationToken);
        return SavingsObjectiveDto.From(objective);
    }
}
