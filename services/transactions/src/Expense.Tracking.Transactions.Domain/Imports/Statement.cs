namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>Tipo do arquivo importado (ADR-0004): extrato de conta ou fatura de cartao.</summary>
public enum StatementKind
{
    BankAccount = 0,
    CreditCard = 1
}

/// <summary>
/// Extrato ou fatura lido de um arquivo, antes de virar lote de importacao. So contem o que foi
/// lido; nada aqui e o arquivo original (ADR-0004, decisao 8).
/// </summary>
public sealed record ParsedStatement(
    StatementKind Kind, string Currency, IReadOnlyList<StatementLine> Lines, StatementTotals? Totals = null)
{
    /// <summary>Moeda quando o arquivo nao declara uma (ADR-0004: sem conversao de moeda).</summary>
    public const string DefaultCurrency = "BRL";
}

/// <summary>
/// RCN-3 (ADR-0011, decisao 2): o que a fatura do banco declara no OFX: o total (<c>LEDGERBAL</c>, sem
/// sinal) e o periodo (<c>DTSTART</c>/<c>DTEND</c> da lista de lancamentos). CSV nao traz nada disso.
/// </summary>
public sealed record StatementTotals(decimal? StatedTotal, DateOnly? Start, DateOnly? End);

/// <summary>
/// Uma linha do arquivo. <see cref="Amount"/> tem sinal: negativo e debito (gasto), positivo e
/// credito. <see cref="ExternalId"/> e a chave de deduplicacao (no OFX, o FITID). Linha com
/// <see cref="Problem"/> e invalida: fica na previa com o motivo, sem derrubar o arquivo.
/// </summary>
public sealed record StatementLine(
    int Number, DateOnly? Date, decimal? Amount, string Description, string? ExternalId, string? Problem)
{
    public bool IsValid => Problem is null;

    public static StatementLine Valid(int number, DateOnly date, decimal amount, string description, string? externalId) =>
        new(number, date, amount, description, externalId, null);

    public static StatementLine Invalid(int number, string description, string problem) =>
        new(number, null, null, description, null, problem);
}

internal static class ImportText
{
    /// <summary>Espacos simples e sem espaco nas pontas; nulo ou em branco vira vazio.</summary>
    public static string NormalizeSpaces(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
