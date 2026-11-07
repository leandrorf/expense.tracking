namespace Expense.Tracking.Identity.Application.OAuth;

/// <summary>Utilitarios para manipular a string de escopos (separada por espaco).</summary>
public static class ScopeParser
{
    private static readonly char[] Separators = [' '];

    public static string[] Parse(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? []
            : scope.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Distinct(StringComparer.Ordinal)
                   .ToArray();

    public static string Join(IEnumerable<string> scopes) => string.Join(' ', scopes);

    /// <summary>Verdadeiro se <paramref name="requested"/> for subconjunto de <paramref name="granted"/>.</summary>
    public static bool IsSubset(IEnumerable<string> requested, IEnumerable<string> granted)
    {
        var grantedSet = new HashSet<string>(granted, StringComparer.Ordinal);
        return requested.All(grantedSet.Contains);
    }
}
