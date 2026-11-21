using Expense.Tracking.Transactions.Domain.Imports.Csv;

namespace Expense.Tracking.Transactions.UnitTests.Imports;

/// <summary>IMP-11: regras do mapeamento de CSV salvo com nome.</summary>
public sealed class SavedCsvMappingTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
    private static readonly CsvColumnMapping Single = new(0, 1, 2, null, null, HasHeader: true, InvertSign: true);

    [Fact]
    public void Create_TrimsTheName_AndKeepsMappingAndHeaders()
    {
        var saved = SavedCsvMapping.Create(Owner, "  Banco X   fatura ", Single, [" Data ", "Descricao", "Valor"], Now).Value;

        Assert.Equal("Banco X fatura", saved.Name);
        Assert.Equal(Single, saved.Mapping);
        Assert.Equal(["Data", "Descricao", "Valor"], saved.Headers);
        Assert.True(saved.HasName("BANCO X FATURA"));
    }

    [Theory]
    [InlineData(null, "import.mapping_name_required")]
    [InlineData("   ", "import.mapping_name_required")]
    public void Create_WithoutName_Fails(string? name, string code) =>
        Assert.Equal(code, SavedCsvMapping.Create(Owner, name, Single, null, Now).Error.Code);

    [Fact]
    public void Create_NameTooLongOrHeadersTooMany_Fails()
    {
        Assert.Equal("import.mapping_name_too_long",
            SavedCsvMapping.Create(Owner, new string('x', 61), Single, null, Now).Error.Code);
        Assert.Equal("import.mapping_headers_invalid",
            SavedCsvMapping.Create(Owner, "X", Single, Enumerable.Repeat("c", 51).ToList(), Now).Error.Code);
        Assert.Equal("import.mapping_headers_invalid",
            SavedCsvMapping.Create(Owner, "X", Single, [new string('c', 101)], Now).Error.Code);
    }

    public static TheoryData<CsvColumnMapping> InvalidMappings => new()
    {
        new CsvColumnMapping(0, 0, 2, null, null, true),            // coluna repetida
        new CsvColumnMapping(-1, 1, 2, null, null, true),           // coluna negativa
        new CsvColumnMapping(0, 1, 2, 3, null, true),               // valor e debito juntos
        new CsvColumnMapping(0, 1, null, 2, null, true),            // debito sem credito
        new CsvColumnMapping(0, 1, 2, null, null, true, ';'),       // separador decimal estranho
    };

    [Theory]
    [MemberData(nameof(InvalidMappings))]
    public void Create_InvalidMapping_Fails(CsvColumnMapping mapping) =>
        Assert.Equal("import.mapping_invalid", SavedCsvMapping.Create(Owner, "X", mapping, null, Now).Error.Code);

    [Fact]
    public void Replace_SwapsMappingAndHeaders_AndBumpsUpdatedAt()
    {
        var saved = SavedCsvMapping.Create(Owner, "Banco", Single, ["A"], Now).Value;
        var split = new CsvColumnMapping(0, 1, null, 2, 3, HasHeader: false);

        Assert.True(saved.Replace(split, null, Now.AddDays(1)).IsSuccess);

        Assert.Equal(split, saved.Mapping);
        Assert.Empty(saved.Headers);
        Assert.Equal(Now, saved.CreatedAtUtc);
        Assert.Equal(Now.AddDays(1), saved.UpdatedAtUtc);
    }
}
