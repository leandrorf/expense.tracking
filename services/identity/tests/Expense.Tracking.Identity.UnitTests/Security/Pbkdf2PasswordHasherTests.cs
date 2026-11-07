using Expense.Tracking.Identity.Infrastructure.Security;

namespace Expense.Tracking.Identity.UnitTests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesExpectedFormat()
    {
        var hash = _hasher.Hash("Password123!");

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
    }

    [Fact]
    public void Hash_IsSaltedAndDifferentEachTime()
    {
        var a = _hasher.Hash("same-password");
        var b = _hasher.Hash("same-password");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("Password123!");

        Assert.True(_hasher.Verify("Password123!", hash));
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("Password123!");

        Assert.False(_hasher.Verify("wrong", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    [InlineData("pbkdf2-sha256$abc$def")]
    public void Verify_WithMalformedHash_ReturnsFalseWithoutThrowing(string malformed)
    {
        Assert.False(_hasher.Verify("whatever", malformed));
    }
}
