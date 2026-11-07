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
        return CreditCardDto.From(result.Value);
    }
}

public sealed class ListCreditCardsHandler(ICreditCardRepository cards)
{
    public async Task<IReadOnlyList<CreditCardDto>> HandleAsync(Guid ownerUserId, Guid? accountId, CancellationToken cancellationToken)
    {
        var items = await cards.ListAsync(ownerUserId, accountId, cancellationToken);
        return items.Select(CreditCardDto.From).ToArray();
    }
}

public sealed class GetCreditCardHandler(ICreditCardRepository cards)
{
    public async Task<Result<CreditCardDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var card = await cards.GetAsync(id, ownerUserId, cancellationToken);
        return card is null
            ? Result.Failure<CreditCardDto>(Error.NotFound("card.not_found", "Cartao nao encontrado."))
            : CreditCardDto.From(card);
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
