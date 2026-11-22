using System.Security.Cryptography;
using System.Text;

namespace Expense.Tracking.Transactions.Domain.AccountingExport;

/// <summary>Totais de uma moeda no momento da exportacao.</summary>
public sealed record AccountingExportTotal(string Currency, decimal Income, decimal Expense);

/// <summary>
/// CTB-6 (ADR-0006, decisao 8): a ultima exportacao de um mes (por usuario, mes e conta), para a
/// tela avisar quando o mes mudou depois. Guarda so quantidades, totais e a impressao digital
/// (SHA-256) das planilhas, nunca o arquivo.
/// </summary>
public sealed class AccountingExportRecord
{
    /// <summary>Conta "todas as contas PJ": o indice unico do MySQL nao compara nulos.</summary>
    public static readonly Guid AllAccounts = Guid.Empty;

    private AccountingExportRecord(Guid id, Guid ownerUserId, DateOnly month, Guid accountId)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        Month = month;
        AccountId = accountId;
        Fingerprint = string.Empty;
        Totals = [];
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Primeiro dia do mes exportado.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>A conta exportada, ou <see cref="AllAccounts"/>.</summary>
    public Guid AccountId { get; private set; }

    public int Lines { get; private set; }

    public int Payments { get; private set; }

    public IReadOnlyList<AccountingExportTotal> Totals { get; private set; }

    /// <summary>SHA-256 (hex) do conteudo das tres planilhas.</summary>
    public string Fingerprint { get; private set; }

    public DateTimeOffset ExportedAtUtc { get; private set; }

    public static AccountingExportRecord Create(
        Guid ownerUserId, DateOnly month, Guid? accountId, int lines, int payments,
        IReadOnlyList<AccountingExportTotal> totals, string fingerprint, DateTimeOffset nowUtc)
    {
        var record = new AccountingExportRecord(Guid.NewGuid(), ownerUserId, month, accountId ?? AllAccounts);
        record.Update(lines, payments, totals, fingerprint, nowUtc);
        return record;
    }

    /// <summary>Exportar de novo o mesmo mes substitui o registro.</summary>
    public void Update(int lines, int payments, IReadOnlyList<AccountingExportTotal> totals, string fingerprint, DateTimeOffset nowUtc)
    {
        Lines = lines;
        Payments = payments;
        Totals = totals.ToList();
        Fingerprint = fingerprint;
        ExportedAtUtc = nowUtc;
    }

    /// <summary>Impressao digital das planilhas: muda se qualquer linha, codigo ou total mudar.</summary>
    public static string FingerprintOf(params string[] sheets)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\u001e', sheets));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
