using System.IO.Compression;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.AccountingExport;
using Expense.Tracking.Transactions.Domain.Common;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Application.AccountingExport;

/// <param name="Month">Primeiro dia do mes exportado.</param>
/// <param name="AccountId">Uma conta PJ so; nulo = todas as contas PJ do usuario.</param>
public sealed record ExportAccountingQuery(Guid OwnerUserId, DateOnly Month, Guid? AccountId);

/// <summary>
/// O ZIP pronto para download. <c>Accounts</c>, <c>Lines</c> e <c>Payments</c> vao para o log, o
/// conteudo nunca.
/// </summary>
public sealed record AccountingExportFile(string FileName, byte[] Content, int Accounts, int Lines, int Payments);

/// <summary>CTB-4: totais de uma moeda, iguais a linha "Total" do <c>resumo.csv</c>.</summary>
public sealed record AccountingTotalsDto(string Currency, decimal Income, decimal Expense, decimal Balance);

/// <summary>CTB-4: o que o ZIP vai ter, sem gera-lo, para a tela mostrar antes de baixar.</summary>
/// <summary>
/// CTB-6: a ultima exportacao do mesmo mes e conta. <paramref name="Changed"/> = o ZIP de agora
/// sairia diferente (lancamento excluido ou importado, codigo contabil trocado...).
/// </summary>
public sealed record AccountingLastExportDto(
    DateTimeOffset ExportedAtUtc, int Lines, int Payments, IReadOnlyList<AccountingTotalsDto> Totals, bool Changed);

/// <param name="UnmappedLines">CTB-5: lancamentos cuja categoria nao tem conta contabil (a coluna sai vazia).</param>
/// <param name="LastExport">CTB-6: nulo quando o mes (com a mesma conta) nunca foi exportado.</param>
public sealed record AccountingExportPreviewDto(
    string FileName,
    int Accounts,
    int Lines,
    int Payments,
    IReadOnlyList<AccountingTotalsDto> Totals,
    int UnmappedLines,
    AccountingLastExportDto? LastExport);

/// <summary>
/// CTB-2 e CTB-3 (ADR-0006): exportacao contabil do mes, so com contas PJ (ativas e inativas), num
/// ZIP com lancamentos, pagamentos de fatura e resumo. Contas e cartoes vem do accounts com o token
/// do usuario; os lancamentos, deste servico, pela data do lancamento (competencia), e os
/// pagamentos, pela data do pagamento. Nada e gravado. A previa (CTB-4) le o mesmo pacote, para os
/// numeros da tela baterem com o arquivo. Cada download grava o registro da CTB-6 (totais e impressao
/// digital, nunca o arquivo), que a previa compara com o mes de agora.
/// </summary>
public sealed class ExportAccountingHandler(
    ITransactionRepository transactions,
    ICardPaymentRepository cardPayments,
    ILedgerAccountRepository ledgerAccounts,
    IAccountingExportRepository exportRecords,
    IAccountsClient accountsClient,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly Error NoBusinessAccount = Error.Validation(
        "accounting_export.no_business_account", "A exportacao contabil e so para contas PJ, e o usuario nao tem nenhuma.");

    public static readonly Error AccountNotBusiness = Error.Validation(
        "accounting_export.account_not_business", "A conta escolhida e PF; a exportacao contabil e so para contas PJ.");

    public async Task<Result<AccountingExportFile>> HandleAsync(ExportAccountingQuery query, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(query, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<AccountingExportFile>(loaded.Error);
        }

        var package = loaded.Value;
        var sheets = Sheets(package);
        var content = Zip(sheets);

        // CTB-6: exportar de novo o mesmo mes (e conta) substitui o registro anterior.
        var fingerprint = AccountingExportRecord.FingerprintOf(sheets.Select(s => s.Csv).ToArray());
        var totals = TotalsOf(package).Select(t => new AccountingExportTotal(t.Currency, t.Income, t.Expense)).ToList();
        var record = await exportRecords.FindAsync(query.OwnerUserId, query.Month, query.AccountId, cancellationToken);
        if (record is null)
        {
            await exportRecords.AddAsync(AccountingExportRecord.Create(query.OwnerUserId, query.Month, query.AccountId,
                package.Entries.Count, package.Payments.Count, totals, fingerprint, timeProvider.GetUtcNow()), cancellationToken);
        }
        else
        {
            record.Update(package.Entries.Count, package.Payments.Count, totals, fingerprint, timeProvider.GetUtcNow());
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            // Dois downloads do mesmo mes ao mesmo tempo: o outro gravou o registro; o arquivo sai igual.
        }

        return new AccountingExportFile(package.FileName, content, package.Accounts, package.Entries.Count, package.Payments.Count);
    }

    public async Task<Result<AccountingExportPreviewDto>> PreviewAsync(ExportAccountingQuery query, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(query, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<AccountingExportPreviewDto>(loaded.Error);
        }

        var package = loaded.Value;
        var record = await exportRecords.FindAsync(query.OwnerUserId, query.Month, query.AccountId, cancellationToken);
        var lastExport = record is null
            ? null
            : new AccountingLastExportDto(
                record.ExportedAtUtc,
                record.Lines,
                record.Payments,
                record.Totals.Select(t => new AccountingTotalsDto(t.Currency, t.Income, t.Expense, t.Income - t.Expense)).ToList(),
                Changed: record.Fingerprint != AccountingExportRecord.FingerprintOf(Sheets(package).Select(s => s.Csv).ToArray()));
        return new AccountingExportPreviewDto(package.FileName, package.Accounts, package.Entries.Count, package.Payments.Count,
            TotalsOf(package), package.Entries.Count(e => e.LedgerAccount is null), lastExport);
    }

    private static (string Name, string Csv)[] Sheets(Package package) =>
    [
        (AccountingEntriesSheet.FileName, AccountingEntriesSheet.Write(package.Entries)),
        (AccountingPaymentsSheet.FileName, AccountingPaymentsSheet.Write(package.Payments)),
        (AccountingSummarySheet.FileName, AccountingSummarySheet.Write(package.Entries)),
    ];

    private static List<AccountingTotalsDto> TotalsOf(Package package) => package.Entries
        .GroupBy(e => e.Currency)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g =>
        {
            var income = g.Where(e => e.Type == TransactionType.Income).Sum(e => e.Amount);
            var expense = g.Where(e => e.Type == TransactionType.Expense).Sum(e => e.Amount);
            return new AccountingTotalsDto(g.Key, income, expense, income - expense);
        })
        .ToList();

    private sealed record Package(string FileName, int Accounts, IReadOnlyList<AccountingEntry> Entries, IReadOnlyList<AccountingPayment> Payments);

    private async Task<Result<Package>> LoadAsync(ExportAccountingQuery query, CancellationToken cancellationToken)
    {
        var accounts = await accountsClient.ListAccountsAsync(cancellationToken);
        IReadOnlyList<AccountInfo> selected;
        if (query.AccountId is { } accountId)
        {
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is null)
            {
                return Result.Failure<Package>(Error.NotFound("account.not_found", "Conta nao encontrada para o usuario."));
            }

            if (!account.IsBusiness)
            {
                return Result.Failure<Package>(AccountNotBusiness);
            }

            selected = [account];
        }
        else
        {
            selected = accounts.Where(a => a.IsBusiness).ToList();
            if (selected.Count == 0)
            {
                return Result.Failure<Package>(NoBusinessAccount);
            }
        }

        var cards = (await accountsClient.ListCardsAsync(cancellationToken)).ToDictionary(c => c.Id);
        var (from, to) = (query.Month, query.Month.AddMonths(1).AddDays(-1));

        // CTB-5: codigo da conta contabil de cada categoria, comparada como foi digitada.
        var ledger = (await ledgerAccounts.ListAsync(query.OwnerUserId, cancellationToken))
            .ToDictionary(l => l.Category, l => l.Code, StringComparer.Ordinal);

        var entries = new List<(DateOnly Date, DateTimeOffset CreatedAt, AccountingEntry Entry)>();
        foreach (var account in selected)
        {
            var listed = await transactions.ListAsync(
                new TransactionQuery(query.OwnerUserId, account.Id, From: from, To: to), cancellationToken);
            entries.AddRange(listed.Select(t => (t.OccurredOn, t.CreatedAtUtc, new AccountingEntry(
                t.OccurredOn, t.Description, t.Category, ledger.GetValueOrDefault(t.Category), t.Type, t.Amount, t.Currency,
                t.PaymentMethod, CardName(t.CreditCardId, cards), t.Installments, account.Name, account.TaxId, t.Notes,
                Imported: t.ImportBatchId is not null || t.ExternalId is not null))));
        }

        var ordered = entries.OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).Select(e => e.Entry).ToList();

        // Pagamento entra quando o cartao ou a conta de origem e uma das contas exportadas (decisao 5).
        var exported = selected.Select(a => a.Id).ToHashSet();
        var accountsById = accounts.ToDictionary(a => a.Id);
        var payments = (await cardPayments.ListPaidBetweenAsync(query.OwnerUserId, from, to, cancellationToken))
            .Where(p => exported.Contains(p.AccountId)
                || (cards.TryGetValue(p.CreditCardId, out var card) && exported.Contains(card.AccountId)))
            .Select(p => new AccountingPayment(
                p.PaidOn,
                CardName(p.CreditCardId, cards) ?? string.Empty,
                p.InvoiceDueMonth,
                accountsById.TryGetValue(p.AccountId, out var origin) ? origin.Name : string.Empty,
                origin?.TaxId,
                p.Amount,
                p.Currency))
            .ToList();

        return new Package(FileName(selected, query.Month), selected.Count, ordered, payments);
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

    private static byte[] Zip((string Name, string Csv)[] files)
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
