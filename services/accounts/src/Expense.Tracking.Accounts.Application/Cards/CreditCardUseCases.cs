using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Application.Contracts;
using Expense.Tracking.Accounts.Domain.Cards;
using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Application.Cards;

public sealed record CreateCreditCardCommand(
    Guid OwnerUserId,
    Guid AccountId,
    string Label,
    string? Brand,
    string LastFourDigits,
    decimal CreditLimit,
    int ClosingDay,
    int DueDay);

public sealed class CreateCreditCardHandler(
    IAccountRepository accounts,
    ICreditCardRepository cards,
    IUnitOfWork unitOfWork,
    AppCalendar calendar,
    TimeProvider timeProvider)
{
    public async Task<Result<CreditCardDto>> HandleAsync(CreateCreditCardCommand command, CancellationToken cancellationToken)
    {
        // A conta precisa existir, estar ativa e pertencer ao mesmo dono.
        if (!await accounts.ExistsActiveAsync(command.AccountId, command.OwnerUserId, cancellationToken))
        {
            return Result.Failure<CreditCardDto>(Error.NotFound("account.not_found", "Conta nao encontrada ou inativa."));
        }

        var result = CreditCard.Create(
            command.OwnerUserId, command.AccountId, command.Label, command.Brand,
            command.LastFourDigits, command.CreditLimit, command.ClosingDay, command.DueDay,
            timeProvider.GetUtcNow());

        if (result.IsFailure)
        {
            return Result.Failure<CreditCardDto>(result.Error);
        }

        await cards.AddAsync(result.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditCardDto.From(result.Value, calendar.Today(timeProvider));
    }
}

public sealed class ListCreditCardsHandler(ICreditCardRepository cards, AppCalendar calendar, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<CreditCardDto>> HandleAsync(Guid ownerUserId, Guid? accountId, CancellationToken cancellationToken)
    {
        var items = await cards.ListAsync(ownerUserId, accountId, cancellationToken);
        var today = calendar.Today(timeProvider);
        return items.Select(c => CreditCardDto.From(c, today)).ToArray();
    }
}

public sealed class GetCreditCardHandler(ICreditCardRepository cards, AppCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<CreditCardDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var card = await cards.GetAsync(id, ownerUserId, cancellationToken);
        return card is null
            ? Result.Failure<CreditCardDto>(CardErrors.NotFound)
            : CreditCardDto.From(card, calendar.Today(timeProvider));
    }
}

internal static class CardErrors
{
    public static readonly Error NotFound = Error.NotFound("card.not_found", "Cartao nao encontrado.");
}

public sealed record EditCreditCardCommand(
    Guid Id, Guid OwnerUserId, string? Label, string? Brand, string? LastFourDigits, decimal CreditLimit);

/// <summary>
/// CYC-2 (ADR-0012, decisao 7): corrige nome, bandeira, final e limite com as regras da criacao. Sem historico: o
/// limite novo vale para o disponivel na hora (o reporting le o cartao a cada consulta).
/// </summary>
public sealed class EditCreditCardHandler(
    ICreditCardRepository cards, IUnitOfWork unitOfWork, AppCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<CreditCardDto>> HandleAsync(EditCreditCardCommand command, CancellationToken cancellationToken)
    {
        var card = await cards.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (card is null)
        {
            return Result.Failure<CreditCardDto>(CardErrors.NotFound);
        }

        var edited = card.Edit(command.Label, command.Brand, command.LastFourDigits, command.CreditLimit, timeProvider.GetUtcNow());
        if (edited.IsFailure)
        {
            return Result.Failure<CreditCardDto>(edited.Error);
        }

        cards.Update(card);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditCardDto.From(card, calendar.Today(timeProvider));
    }
}

public sealed record ChangeCreditCardCycleCommand(Guid Id, Guid OwnerUserId, int ClosingDay, int DueDay, DateOnly FromDueMonth);

/// <summary>
/// CYC-3 (ADR-0012, decisoes 3 e 4): troca o ciclo a partir de uma fatura ainda aberta ou futura; as trocas
/// pendentes sao substituidas. Faturas fechadas nunca mudam.
/// </summary>
public sealed class ChangeCreditCardCycleHandler(
    ICreditCardRepository cards, IUnitOfWork unitOfWork, AppCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<CreditCardDto>> HandleAsync(ChangeCreditCardCycleCommand command, CancellationToken cancellationToken)
    {
        var card = await cards.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (card is null)
        {
            return Result.Failure<CreditCardDto>(CardErrors.NotFound);
        }

        var today = calendar.Today(timeProvider);
        var changed = card.ChangeCycle(command.ClosingDay, command.DueDay, command.FromDueMonth, today, timeProvider.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result.Failure<CreditCardDto>(changed.Error);
        }

        cards.Update(card);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditCardDto.From(card, today);
    }
}

/// <summary>CYC-3 (ADR-0012, decisao 4): desfaz uma troca de ciclo que ainda nao comecou a valer.</summary>
public sealed class RemoveCreditCardCycleHandler(
    ICreditCardRepository cards, IUnitOfWork unitOfWork, AppCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<CreditCardDto>> HandleAsync(Guid id, Guid ownerUserId, DateOnly fromDueMonth, CancellationToken cancellationToken)
    {
        var card = await cards.GetAsync(id, ownerUserId, cancellationToken);
        if (card is null)
        {
            return Result.Failure<CreditCardDto>(CardErrors.NotFound);
        }

        var today = calendar.Today(timeProvider);
        var removed = card.RemoveCycleChange(fromDueMonth, today, timeProvider.GetUtcNow());
        if (removed.IsFailure)
        {
            return Result.Failure<CreditCardDto>(removed.Error);
        }

        cards.Update(card);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditCardDto.From(card, today);
    }
}

public sealed class DeactivateCreditCardHandler(ICreditCardRepository cards, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var card = await cards.GetAsync(id, ownerUserId, cancellationToken);
        if (card is null)
        {
            return Result.Failure(Error.NotFound("card.not_found", "Cartao nao encontrado."));
        }

        card.Deactivate(timeProvider.GetUtcNow());
        cards.Update(card);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
