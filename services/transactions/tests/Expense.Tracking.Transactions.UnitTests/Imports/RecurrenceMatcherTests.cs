using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>REC-9 (ADR-0008, decisao 11): qual linha do extrato e qual ocorrencia em aberto.</summary>
public sealed class RecurrenceMatcherTests
{
    private static readonly DateOnly Jan10 = new(2030, 1, 10);

    private static RecurrenceCandidate Rent(string period = "2030-01", DateOnly? on = null, decimal amount = 2000m) =>
        new(Guid.NewGuid(), period, on ?? Jan10, TransactionType.Expense, amount, "Aluguel", "Moradia");

    private static (int, TransactionType, decimal, DateOnly) Line(int number, decimal amount, DateOnly date,
        TransactionType type = TransactionType.Expense) => (number, type, amount, date);

    [Theory]
    [InlineData(2200, 0, true)]   // +10%: no limite
    [InlineData(1800, 0, true)]   // -10%: no limite
    [InlineData(2200.01, 0, false)]
    [InlineData(2000, 5, true)]   // 5 dias depois
    [InlineData(2000, -5, true)]  // 5 dias antes
    [InlineData(2000, 6, false)]
    public void Matches_WithinTenPercentAndFiveDays(double amount, int days, bool matches)
    {
        var result = RecurrenceMatcher.Match([Line(1, (decimal)amount, Jan10.AddDays(days))], [Rent()]);

        Assert.Equal(matches, result.ContainsKey(1));
    }

    [Fact]
    public void TheTypeMustMatch()
    {
        var result = RecurrenceMatcher.Match([Line(1, 2000m, Jan10, TransactionType.Income)], [Rent()]);

        Assert.Empty(result);
    }

    [Fact]
    public void EachOccurrenceGoesToOneLine_AndALinePicksTheNearestDate()
    {
        var (january, february) = (Rent("2030-01", Jan10), Rent("2030-02", new DateOnly(2030, 2, 10)));

        var result = RecurrenceMatcher.Match(
            [Line(1, 2000m, new DateOnly(2030, 1, 12)), Line(2, 2000m, new DateOnly(2030, 1, 11)), Line(3, 2000m, new DateOnly(2030, 2, 8))],
            [february, january]);

        // A linha 1 vem primeiro e leva janeiro; a 2 fica sem (fevereiro esta longe); a 3 leva fevereiro.
        Assert.Equal(january, result[1]);
        Assert.False(result.ContainsKey(2));
        Assert.Equal(february, result[3]);
    }
}
