using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Contracts;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.Transactions;

public sealed record CreateTransactionCommand(
    Guid OwnerUserId,
    Guid AccountId,
    TransactionType Type,
    PaymentMethod PaymentMethod,
    Guid? CreditCardId,
    string Description,
    string Category,
    decimal Amount,
    string? Currency,
    DateOnly OccurredOn,
    string? Notes);

public sealed class CreateTransactionHandler(
    ITransactionRepository transactions,
    IAccountsClient accountsClient,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<TransactionDto>> HandleAsync(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        var result = Transaction.Create(
            command.OwnerUserId, command.AccountId, command.Type, command.PaymentMethod, command.CreditCardId,
            command.Description, command.Category, command.Amount, command.Currency, command.OccurredOn,
            command.Notes, timeProvider.GetUtcNow());

        if (result.IsFailure)
        {
            return Result.Failure<TransactionDto>(result.Error);
        }

        // Valida referencias no servico accounts (propriedade garantida pelo token encaminhado).
        if (!await accountsClient.AccountExistsAsync(command.AccountId, cancellationToken))
        {
            return Result.Failure<TransactionDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
        }

        if (command.CreditCardId is { } cardId
            && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<TransactionDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        await transactions.AddAsync(result.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionDto.From(result.Value);
    }
}

public sealed record ListTransactionsCommand(
    Guid OwnerUserId,
    Guid? AccountId,
    string? Category,
    DateOnly? From,
    DateOnly? To);

public sealed class ListTransactionsHandler(ITransactionRepository transactions)
{
    public async Task<IReadOnlyList<TransactionDto>> HandleAsync(ListTransactionsCommand command, CancellationToken cancellationToken)
    {
        var items = await transactions.ListAsync(
            new TransactionQuery(command.OwnerUserId, command.AccountId, command.Category, command.From, command.To),
            cancellationToken);

        return items.Select(TransactionDto.From).ToArray();
    }
}

public sealed class GetTransactionHandler(ITransactionRepository transactions)
{
    public async Task<Result<TransactionDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetAsync(id, ownerUserId, cancellationToken);
        return transaction is null
            ? Result.Failure<TransactionDto>(Error.NotFound("transaction.not_found", "Lancamento nao encontrado."))
            : TransactionDto.From(transaction);
    }
}

public sealed class DeleteTransactionHandler(ITransactionRepository transactions, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetAsync(id, ownerUserId, cancellationToken);
        if (transaction is null)
        {
            return Result.Failure(Error.NotFound("transaction.not_found", "Lancamento nao encontrado."));
        }

        transactions.Remove(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
