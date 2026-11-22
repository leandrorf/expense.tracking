using System.IO.Compression;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Application.AccountingExport;

/// <param name="Month">Primeiro dia do mes exportado.</param>
/// <param name="AccountId">Uma conta PJ so; nulo = todas as contas PJ do usuario.</param>
public sealed record ExportAccountingQuery(Guid OwnerUserId, DateOnly Month, Guid? AccountId);

/// <summary>O ZIP pronto para download. <c>Accounts</c> e <c>Lines</c> vao para o log, o conteudo nunca.</summary>
public sealed record AccountingExportFile(string FileName, byte[] Content, int Accounts, int Lines);

/// <summary>
/// CTB-2 (ADR-0006): exportacao contabil do mes, so com contas PJ (ativas e inativas). Contas e
/// cartoes vem do accounts com o token do usuario; os lancamentos, deste servico, pela data do
/// lancamento (competencia). Nada e gravado.
/// </summary>
public sealed class ExportAccountingHandler(ITransactionRepository transactions, IAccountsClient accountsClient)
{
    public static readonly Error NoBusinessAccount = Error.Validation(
        "accounting_export.no_business_account", "A exportacao contabil e so para contas PJ, e o usuario nao tem nenhuma.");

    public static readonly Error AccountNotBusiness = Error.Validation(
        "accounting_export.account_not_business", "A conta escolhida e PF; a exportacao contabil e so para contas PJ.");

    public async Task<Result<AccountingExportFile>> HandleAsync(ExportAccountingQuery query, CancellationToken cancellationToken)
    {
        var accounts = await accountsClient.ListAccountsAsync(cancellationToken);
        IReadOnlyList<AccountInfo> selected;
        if (query.AccountId is { } accountId)
        {
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is null)
            {
                return Result.Failure<AccountingExportFile>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
            }

            if (!account.IsBusiness)
            {
                return Result.Failure<AccountingExportFile>(AccountNotBusiness);
            }

            selected = [account];
        }
        else
        {
            selected = accounts.Where(a => a.IsBusiness).ToList();
            if (selected.Count == 0)
            {
                return Result.Failure<AccountingExportFile>(NoBusinessAccount);
            }
        }

        var cards = (await accountsClient.ListCardsAsync(cancellationToken)).ToDictionary(c => c.Id);
        var (from, to) = (query.Month, query.Month.AddMonths(1).AddDays(-1));

        var entries = new List<(DateOnly Date, DateTimeOffset CreatedAt, AccountingEntry Entry)>();
        foreach (var account in selected)
        {
            var listed = await transactions.ListAsync(
                new TransactionQuery(query.OwnerUserId, account.Id, From: from, To: to), cancellationToken);
            entries.AddRange(listed.Select(t => (t.OccurredOn, t.CreatedAtUtc, new AccountingEntry(
                t.OccurredOn, t.Description, t.Category, LedgerAccount: null, t.Type, t.Amount, t.Currency,
                t.PaymentMethod, CardName(t.CreditCardId, cards), t.Installments, account.Name, account.TaxId, t.Notes,
                Imported: t.ImportBatchId is not null || t.ExternalId is not null))));
        }

        var sheet = AccountingEntriesSheet.Write(entries.OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).Select(e => e.Entry));
        return new AccountingExportFile(
            FileName(selected, query.Month), Zip((AccountingEntriesSheet.FileName, sheet)), selected.Count, entries.Count);
    }

    /// <summary><c>contabil-&lt;CNPJ&gt;-&lt;aaaa-mm&gt;.zip</c>; sem o CNPJ quando as contas tem mais de um.</summary>
    public static string FileName(IReadOnlyList<AccountInfo> accounts, DateOnly month)
    {
        var taxIds = accounts.Select(a => a.TaxId).Distinct().ToList();
        var period = month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        return taxIds is [{ Length: > 0 } taxId] ? $"contabil-{taxId}-{period}.zip" : $"contabil-{period}.zip";
    }

    private static string? CardName(Guid? cardId, IReadOnlyDictionary<Guid, CardInfo> cards) =>
        cardId is { } id && cards.TryGetValue(id, out var card) ? $"{card.Label} final {card.LastFourDigits}" : null;

    private static byte[] Zip(params (string Name, string Csv)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, csv) in files)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(AccountingCsv.ToBytes(csv));
            }
        }

        return buffer.ToArray();
    }
}
