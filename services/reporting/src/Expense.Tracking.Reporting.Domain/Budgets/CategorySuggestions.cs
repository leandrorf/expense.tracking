namespace Expense.Tracking.Reporting.Domain.Budgets;

/// <param name="Category">Categoria como foi digitada.</param>
/// <param name="Count">Quantas vezes essa grafia foi usada.</param>
public sealed record CategoryUsage(string Category, int Count);

/// <summary>
/// BDG-7: categorias ja usadas pelo usuario, sem repetir variacoes de maiuscula, acento e espaco
/// (mesma chave do <see cref="CategoryKey"/>). Cada categoria aparece pela grafia mais usada.
/// </summary>
public static class CategorySuggestions
{
    public static IReadOnlyList<string> Build(IEnumerable<CategoryUsage> usages) =>
        usages
            .Where(u => !string.IsNullOrWhiteSpace(u.Category))
            .GroupBy(u => CategoryKey.Normalize(u.Category))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(PreferredSpelling)
            .ToList();

    /// <summary>A grafia mais usada entre variantes da mesma categoria (empate: ordem ordinal).</summary>
    public static string PreferredSpelling(IEnumerable<CategoryUsage> sameCategory) =>
        sameCategory
            .GroupBy(u => u.Category.Trim())
            .Select(v => (Spelling: v.Key, Count: v.Sum(u => u.Count)))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Spelling, StringComparer.Ordinal)
            .First().Spelling;
}
