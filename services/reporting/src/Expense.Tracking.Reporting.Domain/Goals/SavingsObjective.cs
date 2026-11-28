using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Goals;

/// <summary>MET-5 (ADR-0013, regra 6): dinheiro guardado (positivo) ou retirado (negativo) de um objetivo.</summary>
public sealed class SavingsContribution
{
    private SavingsContribution()
    {
    }

    internal SavingsContribution(Guid id, DateOnly occurredOn, decimal amount, string? note, DateTimeOffset createdAt)
    {
        Id = id;
        OccurredOn = occurredOn;
        Amount = amount;
        Note = note;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    /// <summary>Valor guardado; negativo = retirada.</summary>
    public decimal Amount { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// MET-5 (ADR-0013, regras 1 e 6): objetivo de economia num escopo PF/PJ, com alvo, prazo opcional e os aportes
/// registrados pelo usuario. O progresso e a soma dos aportes, que nunca fica negativa. Aporte nao e lancamento:
/// nao entra no Resumo, nos orcamentos, na previsao nem no resultado do mes.
/// </summary>
public sealed class SavingsObjective
{
    public const int NameMaxLength = 60;
    public const int NoteMaxLength = 120;
    public const decimal MaxAmount = 1_000_000_000m;

    private readonly List<SavingsContribution> _contributions = [];

    private SavingsObjective()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public GoalScope Scope { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Target { get; private set; }

    /// <summary>Mes do prazo (dia 1); nulo = sem prazo.</summary>
    public DateOnly? Deadline { get; private set; }

    /// <summary>Quando foi arquivado; nulo enquanto ativo.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>Aportes do mais antigo ao mais novo.</summary>
    public IReadOnlyList<SavingsContribution> Contributions =>
        _contributions.OrderBy(c => c.OccurredOn).ThenBy(c => c.CreatedAt).ToList();

    /// <summary>O guardado: soma dos aportes (nunca negativa).</summary>
    public decimal Saved => _contributions.Sum(c => c.Amount);

    /// <param name="today">Hoje no fuso do app: o prazo nao pode ser de um mes que ja passou.</param>
    public static Result<SavingsObjective> Create(
        Guid ownerUserId, GoalScope scope, string? name, decimal target, DateOnly? deadline, DateOnly today, DateTimeOffset now)
    {
        if (!Enum.IsDefined(scope))
        {
            return Result.Failure<SavingsObjective>(GoalErrors.ScopeInvalid);
        }

        if (ValidateDetails(name, target, deadline, today) is { } error)
        {
            return Result.Failure<SavingsObjective>(error);
        }

        return new SavingsObjective
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Scope = scope,
            Name = name!.Trim(),
            Target = target,
            Deadline = deadline is { } d ? FirstDayOf(d) : null,
            CreatedAt = now
        };
    }

    /// <summary>MET-5: corrige nome, alvo e prazo. Manter o prazo que ja passou e permitido; mudar para um mes passado, nao.</summary>
    public Result Edit(string? name, decimal target, DateOnly? deadline, DateOnly today)
    {
        if (IsArchived)
        {
            return Result.Failure(GoalErrors.Archived);
        }

        var month = deadline is { } d ? FirstDayOf(d) : (DateOnly?)null;
        var keptDeadline = month == Deadline;
        if (ValidateDetails(name, target, keptDeadline ? null : month, today) is { } error)
        {
            return Result.Failure(error);
        }

        Name = name!.Trim();
        Target = target;
        Deadline = month;
        return Result.Success();
    }

    public Result<SavingsContribution> AddContribution(DateOnly occurredOn, decimal amount, string? note, DateOnly today, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return Result.Failure<SavingsContribution>(GoalErrors.Archived);
        }

        if (amount == 0m || Math.Abs(amount) > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<SavingsContribution>(GoalErrors.ContributionInvalid);
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > NoteMaxLength })
        {
            return Result.Failure<SavingsContribution>(GoalErrors.ContributionInvalid);
        }

        if (occurredOn > today)
        {
            return Result.Failure<SavingsContribution>(GoalErrors.ContributionInFuture);
        }

        if (Saved + amount < 0m)
        {
            return Result.Failure<SavingsContribution>(GoalErrors.BalanceNegative);
        }

        var contribution = new SavingsContribution(Guid.NewGuid(), occurredOn, amount, trimmed, now);
        _contributions.Add(contribution);
        return contribution;
    }

    public Result RemoveContribution(Guid contributionId)
    {
        if (IsArchived)
        {
            return Result.Failure(GoalErrors.Archived);
        }

        if (_contributions.Find(c => c.Id == contributionId) is not { } contribution)
        {
            return Result.Failure(GoalErrors.ContributionNotFound);
        }

        if (Saved - contribution.Amount < 0m)
        {
            return Result.Failure(GoalErrors.BalanceNegative);
        }

        _contributions.Remove(contribution);
        return Result.Success();
    }

    /// <summary>Arquivar e reabrir sao idempotentes.</summary>
    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Unarchive() => ArchivedAt = null;

    private static Error? ValidateDetails(string? name, decimal target, DateOnly? newDeadline, DateOnly today)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > NameMaxLength)
        {
            return GoalErrors.NameInvalid;
        }

        if (target <= 0m || target > MaxAmount || decimal.Round(target, 2) != target)
        {
            return GoalErrors.TargetInvalid;
        }

        return newDeadline is { } d && FirstDayOf(d) < FirstDayOf(today) ? GoalErrors.DeadlineInvalid : null;
    }

    private static DateOnly FirstDayOf(DateOnly month) => new(month.Year, month.Month, 1);
}
