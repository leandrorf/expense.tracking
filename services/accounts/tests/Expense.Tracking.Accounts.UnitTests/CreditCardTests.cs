using Expense.Tracking.Accounts.Domain.Cards;

namespace Expense.Tracking.Accounts.UnitTests;

public sealed class CreditCardTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void Create_Valid_Succeeds()
    {
        var result = CreditCard.Create(Owner, AccountId, "Nubank", "Mastercard", "1234", 5000m, 10, 17, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("1234", result.Value.LastFourDigits);
        Assert.Equal("Mastercard", result.Value.Brand);
    }

    [Fact]
    public void Create_DefaultsBrandWhenMissing()
    {
        var result = CreditCard.Create(Owner, AccountId, "Cartao", null, "9999", 1000m, 5, 12, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Outro", result.Value.Brand);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abcd")]
    public void Create_WithInvalidLastFour_Fails(string lastFour)
    {
        var result = CreditCard.Create(Owner, AccountId, "Cartao", "Visa", lastFour, 1000m, 5, 12, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("card.last_four_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 29)]
    [InlineData(31, 10)]
    public void Create_WithInvalidDays_Fails(int closing, int due)
    {
        var result = CreditCard.Create(Owner, AccountId, "Cartao", "Visa", "1234", 1000m, closing, due, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("card.day_invalid", result.Error.Code);
    }

    [Fact]
    public void Create_WithNegativeLimit_Fails()
    {
        var result = CreditCard.Create(Owner, AccountId, "Cartao", "Visa", "1234", -1m, 5, 12, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("card.limit_negative", result.Error.Code);
    }
}
