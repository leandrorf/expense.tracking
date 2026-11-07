using Expense.Tracking.Identity.Domain.Users;

namespace Expense.Tracking.Identity.UnitTests.Domain;

public sealed class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("a.b-c@mail.co")]
    public void Create_WithValidEmail_Succeeds(string input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value.Value);
    }

    [Fact]
    public void Create_NormalizesTrimAndLowercase()
    {
        var result = Email.Create("  USER@Example.COM  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("user@example.com", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@domain")]
    [InlineData("@no-local.com")]
    public void Create_WithInvalidEmail_Fails(string? input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = Email.Create("user@example.com").Value;
        var b = Email.Create("USER@EXAMPLE.COM").Value;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
