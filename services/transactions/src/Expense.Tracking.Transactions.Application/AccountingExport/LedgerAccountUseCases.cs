using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Application.AccountingExport;

/// <summary>
/// CTB-5: uma categoria usada em contas PJ (ou ja associada), com a conta contabil quando houver.
/// <paramref name="Lines"/> e quantos lancamentos PJ tem a categoria, em qualquer mes.
/// </summary>
public sealed record LedgerAccountDto(string Category, string? Code, string? Name, int Lines);

public sealed record SetLedgerAccountCommand(Guid OwnerUserId, string? Category, string? Code, string? Name);

/// <summary>
/// CTB-5 (ADR-0006, decisao 7): categorias das contas PJ do usuario com a conta contabil de cada
/// uma. Categoria associada que deixou de ser usada continua na lista (com 0), para poder sair.
/// </summary>
public sealed class ListLedgerAccountsHandler(
    ITransactionRepository transactions, ILedgerAccountRepository ledgerAccounts, IAccountsClient accountsClient)
{
    public async Task<IReadOnlyList<LedgerAccountDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var business = (await accountsClient.ListAccountsAsync(cancellationToken)).Where(a => a.IsBusiness).Select(a => a.Id).ToList();
        var used = business.Count == 0
            ? []
            : (await transactions.ListCategoriesAsync(ownerUserId, business, cancellationToken))
                .GroupBy(c => c, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var mapped = (await ledgerAccounts.ListAsync(ownerUserId, cancellationToken)).ToDictionary(l => l.Category, StringComparer.Ordinal);

        return used.Keys.Union(mapped.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(category =>
            {
                var ledger = mapped.GetValueOrDefault(category);
                return new LedgerAccountDto(category, ledger?.Code, ledger?.Name, used.GetValueOrDefault(category));
            })
            .ToList();
    }
}

/// <summary>CTB-5: associa (ou troca) a conta contabil de uma categoria.</summary>
public sealed class SetLedgerAccountHandler(ILedgerAccountRepository ledgerAccounts, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result<LedgerAccountDto>> HandleAsync(SetLedgerAccountCommand command, CancellationToken cancellationToken)
    {
        var category = LedgerAccount.NormalizeCategory(command.Category);
        if (category.IsFailure)
        {
            return Result.Failure<LedgerAccountDto>(category.Error);
        }

        var all = await ledgerAccounts.ListAsync(command.OwnerUserId, cancellationToken);
        var existing = all.FirstOrDefault(l => string.Equals(l.Category, category.Value, StringComparison.Ordinal));
        if (existing is not null)
        {
            var changed = existing.Change(command.Code, command.Name, timeProvider.GetUtcNow());
            if (changed.IsFailure)
            {
                return Result.Failure<LedgerAccountDto>(changed.Error);
            }
        }
        else
        {
            if (all.Count >= LedgerAccount.MaxPerUser)
            {
                return Result.Failure<LedgerAccountDto>(Error.Validation(
                    "ledger_account.limit_reached", $"No maximo {LedgerAccount.MaxPerUser} contas contabeis por usuario."));
            }

            var created = LedgerAccount.Create(command.OwnerUserId, category.Value, command.Code, command.Name, timeProvider.GetUtcNow());
            if (created.IsFailure)
            {
                return Result.Failure<LedgerAccountDto>(created.Error);
            }

            existing = created.Value;
            await ledgerAccounts.AddAsync(existing, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LedgerAccountDto(existing.Category, existing.Code, existing.Name, Lines: 0);
    }
}

/// <summary>CTB-5: tira a conta contabil de uma categoria; a coluna volta a sair vazia.</summary>
public sealed class DeleteLedgerAccountHandler(ILedgerAccountRepository ledgerAccounts, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid ownerUserId, string? category, CancellationToken cancellationToken)
    {
        var existing = (await ledgerAccounts.ListAsync(ownerUserId, cancellationToken))
            .FirstOrDefault(l => string.Equals(l.Category, category?.Trim(), StringComparison.Ordinal));
        if (existing is null)
        {
            return Result.Failure(Error.NotFound("ledger_account.not_found", "Categoria sem conta contabil."));
        }

        ledgerAccounts.Remove(existing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
