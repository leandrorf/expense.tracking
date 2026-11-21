using Expense.Tracking.Reporting.Domain.Budgets;

namespace Expense.Tracking.Reporting.UnitTests;

public sealed class CategorySuggestionsTests
{
    [Fact]
    public void Variants_AreSuggestedOnce_ByTheMostUsedSpelling()
    {
        var suggestions = CategorySuggestions.Build(
        [
            new CategoryUsage("Alimentação", 3),
            new CategoryUsage("alimentacao ", 1),
            new CategoryUsage("ALIMENTAÇÃO", 1),
            new CategoryUsage("Combustível", 2),
        ]);

        Assert.Equal(["Alimentação", "Combustível"], suggestions);
    }

    [Fact]
    public void Spellings_DifferingOnlyBySurroundingSpaces_AddUp()
    {
        var suggestions = CategorySuggestions.Build(
        [
            new CategoryUsage("viagens", 2),
            new CategoryUsage(" Viagens", 2),
            new CategoryUsage("Viagens ", 1),
        ]);

        Assert.Equal(["Viagens"], suggestions);
    }

    [Fact]
    public void Tie_PicksTheOrdinalFirst_AndBlankCategoriesAreIgnored()
    {
        var suggestions = CategorySuggestions.Build(
        [
            new CategoryUsage("saude", 1),
            new CategoryUsage("Saúde", 1),
            new CategoryUsage("   ", 4),
        ]);

        Assert.Equal(["Saúde"], suggestions);
    }

    [Fact]
    public void Suggestions_AreSortedIgnoringCaseAndAccents()
    {
        var suggestions = CategorySuggestions.Build(
        [
            new CategoryUsage("lazer", 1),
            new CategoryUsage("Água", 1),
            new CategoryUsage("Educação", 1),
        ]);

        Assert.Equal(["Água", "Educação", "lazer"], suggestions);
    }
}
