using Expense.Tracking.Identity.Application.Users;
using Expense.Tracking.Identity.Domain.Common;
using Expense.Tracking.Identity.UnitTests.TestDoubles;

namespace Expense.Tracking.Identity.UnitTests.Application;

public sealed class RegisterUserHandlerTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly NoOpUnitOfWork _unitOfWork = new();
    private readonly RegisterUserHandler _handler;

    public RegisterUserHandlerTests()
    {
        _handler = new RegisterUserHandler(
            _users,
            new FakePasswordHasher(),
            _unitOfWork,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
    }

    [Fact]
    public async Task Register_WithValidData_PersistsUser()
    {
        var result = await _handler.HandleAsync(
            new RegisterUserCommand("user@example.com", "Password123!", "User"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("user@example.com", result.Value.Email);
        Assert.Single(_users.Users);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Register_WithShortPassword_Fails()
    {
        var result = await _handler.HandleAsync(
            new RegisterUserCommand("user@example.com", "short", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("password.too_short", result.Error.Code);
        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var command = new RegisterUserCommand("dupe@example.com", "Password123!", null);
        await _handler.HandleAsync(command, CancellationToken.None);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Register_WithInvalidEmail_Fails()
    {
        var result = await _handler.HandleAsync(
            new RegisterUserCommand("not-an-email", "Password123!", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("email.invalid", result.Error.Code);
    }
}
