using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Application.Contracts;
using Expense.Tracking.Transactions.Application.Events;
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
    string? Notes,
    int Installments = 1);

public sealed class CreateTransactionHandler(
    ITransactionRepository transactions,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<TransactionDto>> HandleAsync(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        var result = Transaction.Create(
            command.OwnerUserId, command.AccountId, command.Type, command.PaymentMethod, command.CreditCardId,
            command.Description, command.Category, command.Amount, command.Currency, command.OccurredOn,
            command.Notes, timeProvider.GetUtcNow(), command.Installments);

        if (result.IsFailure)
        {
            return Result.Failure<TransactionDto>(result.Error);
        }

        // Valida referencias no servico accounts (propriedade garantida pelo token encaminhado).
        // A mesma consulta traz o escopo PF/PJ da conta, que vai no evento (EVT-1).
        var accountScope = await accountsClient.FindAccountScopeAsync(command.AccountId, cancellationToken);
        if (accountScope is null)
        {
            return Result.Failure<TransactionDto>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
        }

        if (command.CreditCardId is { } cardId
            && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<TransactionDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        await transactions.AddAsync(result.Value, cancellationToken);
        outbox.Add(TransactionRegistered.From(result.Value, accountScope));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionDto.From(result.Value);
    }
}

/// <summary>LNC-3: o lancamento inteiro como deve ficar, mais a versao que o cliente leu.</summary>
public sealed record UpdateTransactionCommand(
    Guid Id,
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
    string? Notes,
    int Installments,
    int Version);

/// <summary>
/// LNC-3 (ADR-0007): corrige um lancamento com as regras da criacao e publica o
/// <see cref="TransactionUpdated"/> no mesmo commit. A conta nao muda; a versao lida precisa ser a
/// atual (concorrencia otimista). Edicao sem mudanca nao sobe a versao nem gera evento.
/// </summary>
public sealed class UpdateTransactionHandler(
    ITransactionRepository transactions,
    IAccountsClient accountsClient,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly Error ConcurrencyConflict = Error.Conflict(
        "transaction.concurrency_conflict", "O lancamento mudou depois que voce o leu. Recarregue e tente de novo.");

    public async Task<Result<TransactionDto>> HandleAsync(UpdateTransactionCommand command, CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetAsync(command.Id, command.OwnerUserId, cancellationToken);
        if (transaction is null)
        {
            return Result.Failure<TransactionDto>(Error.NotFound("transaction.not_found", "Lancamento nao encontrado."));
        }

        if (command.AccountId != transaction.AccountId)
        {
            return Result.Failure<TransactionDto>(Error.Validation(
                "transaction.account_immutable", "A conta de um lancamento nao muda: exclua e lance de novo na outra conta."));
        }

        if (command.Version != transaction.Version)
        {
            return Result.Failure<TransactionDto>(ConcurrencyConflict);
        }

        var previousCard = transaction.CreditCardId;
        var edited = transaction.Edit(
            command.Type, command.PaymentMethod, command.CreditCardId, command.Description, command.Category,
            command.Amount, command.Currency, command.OccurredOn, command.Notes, command.Installments, timeProvider.GetUtcNow());
        if (edited.IsFailure)
        {
            return Result.Failure<TransactionDto>(edited.Error);
        }

        if (!edited.Value)
        {
            return TransactionDto.From(transaction);
        }

        if (transaction.CreditCardId is { } cardId && cardId != previousCard
            && !await accountsClient.CreditCardExistsAsync(cardId, cancellationToken))
        {
            return Result.Failure<TransactionDto>(Error.NotFound("card.not_found", "Cartao nao encontrado para o usuario."));
        }

        outbox.Add(TransactionUpdated.From(transaction));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            // Outra edicao gravou entre a leitura e a gravacao (token de concorrencia da versao).
            return Result.Failure<TransactionDto>(ConcurrencyConflict);
        }

        return TransactionDto.From(transaction);
    }
}

/// <summary>LNC-7: um lancamento selecionado na lista e a versao que a tela leu.</summary>
public sealed record RecategorizeItem(Guid Id, int Version);

public sealed record RecategorizeTransactionsCommand(Guid OwnerUserId, string Category, IReadOnlyList<RecategorizeItem> Items);

/// <summary>Um lancamento do lote que a regra de hoje recusa (ex.: dado antigo que nao valida mais).</summary>
public sealed record RecategorizeRejection(Guid Id, string Code);

/// <summary>
/// O que aconteceu com cada lancamento do lote: mudou, ja estava na categoria, mudou em outra aba
/// (vem o estado atual, sem sobrescrever), nao existe (ou e de outro usuario) ou foi recusado.
/// </summary>
public sealed record RecategorizeTransactionsResult(
    IReadOnlyList<TransactionDto> Updated,
    IReadOnlyList<TransactionDto> Unchanged,
    IReadOnlyList<TransactionDto> Conflicts,
    IReadOnlyList<Guid> NotFound,
    IReadOnlyList<RecategorizeRejection> Rejected);

/// <summary>
/// LNC-7: recategoriza varios lancamentos de uma vez. Cada um segue a concorrencia otimista da
/// edicao (LNC-3): so muda o que esta na versao lida, e cada mudanca sai como um
/// <see cref="TransactionUpdated"/> no mesmo commit. Lancamento com versao antiga e informado, nao
/// sobrescrito. Se outra gravacao passar entre a leitura e o commit, nada do lote e gravado (409).
/// </summary>
public sealed class RecategorizeTransactionsHandler(
    ITransactionRepository transactions,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int MaxItems = 500;

    public async Task<Result<RecategorizeTransactionsResult>> HandleAsync(
        RecategorizeTransactionsCommand command, CancellationToken cancellationToken)
    {
        var category = Transaction.ValidateCategory(command.Category);
        if (category.IsFailure)
        {
            return Result.Failure<RecategorizeTransactionsResult>(category.Error);
        }

        if (command.Items.Count == 0)
        {
            return Result.Failure<RecategorizeTransactionsResult>(Error.Validation(
                "transaction.batch_empty", "Selecione pelo menos um lancamento."));
        }

        if (command.Items.Count > MaxItems)
        {
            return Result.Failure<RecategorizeTransactionsResult>(Error.Validation(
                "transaction.batch_too_large", $"Recategorize no maximo {MaxItems} lancamentos por vez."));
        }

        if (command.Items.Select(i => i.Id).Distinct().Count() != command.Items.Count)
        {
            return Result.Failure<RecategorizeTransactionsResult>(Error.Validation(
                "transaction.batch_duplicated", "O mesmo lancamento aparece mais de uma vez no lote."));
        }

        var ids = command.Items.Select(i => i.Id).ToArray();
        var found = (await transactions.ListByIdsAsync(command.OwnerUserId, ids, cancellationToken)).ToDictionary(t => t.Id);

        var (updated, unchanged, conflicts) = (new List<Transaction>(), new List<Transaction>(), new List<Transaction>());
        var (notFound, rejected) = (new List<Guid>(), new List<RecategorizeRejection>());
        var now = timeProvider.GetUtcNow();

        foreach (var item in command.Items)
        {
            if (!found.TryGetValue(item.Id, out var transaction))
            {
                notFound.Add(item.Id);
                continue;
            }

            if (item.Version != transaction.Version)
            {
                conflicts.Add(transaction);
                continue;
            }

            var changed = transaction.Recategorize(category.Value, now);
            if (changed.IsFailure)
            {
                rejected.Add(new RecategorizeRejection(transaction.Id, changed.Error.Code));
            }
            else if (changed.Value)
            {
                outbox.Add(TransactionUpdated.From(transaction));
                updated.Add(transaction);
            }
            else
            {
                unchanged.Add(transaction);
            }
        }

        if (updated.Count > 0)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (PersistenceConflictException)
            {
                return Result.Failure<RecategorizeTransactionsResult>(UpdateTransactionHandler.ConcurrencyConflict);
            }
        }

        return new RecategorizeTransactionsResult(
            updated.Select(TransactionDto.From).ToArray(),
            unchanged.Select(TransactionDto.From).ToArray(),
            conflicts.Select(TransactionDto.From).ToArray(),
            notFound,
            rejected);
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

public sealed class DeleteTransactionHandler(
    ITransactionRepository transactions,
    IEventOutbox outbox,
    IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetAsync(id, ownerUserId, cancellationToken);
        if (transaction is null)
        {
            return Result.Failure(Error.NotFound("transaction.not_found", "Lancamento nao encontrado."));
        }

        transactions.Remove(transaction);
        // A exclusao e fisica: o evento e o unico registro de que o lancamento existiu.
        outbox.Add(new TransactionDeleted(transaction.Id, transaction.OwnerUserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
