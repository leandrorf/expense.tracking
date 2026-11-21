using Expense.Tracking.Transactions.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

internal sealed class UnitOfWork(TransactionsDbContext dbContext) : IUnitOfWork
{
    /// <summary>ER_DUP_ENTRY: violacao de indice unico no MySQL.</summary>
    private const int MySqlDuplicateEntry = 1062;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceConflictException("Registro alterado por outra operacao.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: MySqlDuplicateEntry })
        {
            throw new PersistenceConflictException("Chave unica ja gravada por outra operacao.", ex);
        }
    }

    public void Reset() => dbContext.ChangeTracker.Clear();
}
