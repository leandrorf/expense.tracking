using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Application.Contracts;
using Expense.Tracking.Accounts.Domain.Accounts;
using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Application.Accounts;

public sealed record CreateAccountCommand(Guid OwnerUserId, string Name, AccountScope Scope, string? TaxId, string? Currency);

public sealed class CreateAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result<AccountDto>> HandleAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var result = Account.Create(command.OwnerUserId, command.Name, command.Scope, command.TaxId, command.Currency, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return Result.Failure<AccountDto>(result.Error);
        }

        await accounts.AddAsync(result.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AccountDto.From(result.Value);
    }
}

public sealed class ListAccountsHandler(IAccountRepository accounts)
{
    public async Task<IReadOnlyList<AccountDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var items = await accounts.ListAsync(ownerUserId, cancellationToken);
        return items.Select(AccountDto.From).ToArray();
    }
}

public sealed class GetAccountHandler(IAccountRepository accounts)
{
    public async Task<Result<AccountDto>> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(id, ownerUserId, cancellationToken);
        return account is null
            ? Result.Failure<AccountDto>(Error.NotFound("account.not_found", "Conta nao encontrada."))
            : AccountDto.From(account);
    }
}

public sealed record RenameAccountCommand(Guid Id, Guid OwnerUserId, string Name);

public sealed class RenameAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result<AccountDto>> HandleAsync(RenameAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDto>(Error.NotFound("account.not_found", "Conta nao encontrada."));
        }

        var rename = account.Rename(command.Name, timeProvider.GetUtcNow());
        if (rename.IsFailure)
        {
            return Result.Failure<AccountDto>(rename.Error);
        }

        accounts.Update(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AccountDto.From(account);
    }
}

public sealed class DeactivateAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(id, ownerUserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(Error.NotFound("account.not_found", "Conta nao encontrada."));
        }

        account.Deactivate(timeProvider.GetUtcNow());
        accounts.Update(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
