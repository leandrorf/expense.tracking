using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Common;
using Expense.Tracking.Identity.Domain.Users;

namespace Expense.Tracking.Identity.Application.Users;

public sealed record RegisterUserCommand(string Email, string Password, string? DisplayName);

public sealed record RegisterUserResult(Guid UserId, string Email, string DisplayName);

/// <summary>Cria um novo usuario com senha hasheada.</summary>
public sealed class RegisterUserHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int MinimumPasswordLength = 8;

    public async Task<Result<RegisterUserResult>> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<RegisterUserResult>(emailResult.Error);
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < MinimumPasswordLength)
        {
            return Result.Failure<RegisterUserResult>(
                Error.Validation("password.too_short", $"A senha deve ter ao menos {MinimumPasswordLength} caracteres."));
        }

        var email = emailResult.Value;

        if (await userRepository.ExistsByEmailAsync(email.Value, cancellationToken))
        {
            return Result.Failure<RegisterUserResult>(
                Error.Conflict("user.email_in_use", "Ja existe um usuario com este e-mail."));
        }

        var hashResult = PasswordHash.Create(passwordHasher.Hash(command.Password));
        if (hashResult.IsFailure)
        {
            return Result.Failure<RegisterUserResult>(hashResult.Error);
        }

        var userResult = User.Register(email, hashResult.Value, command.DisplayName, timeProvider.GetUtcNow());
        if (userResult.IsFailure)
        {
            return Result.Failure<RegisterUserResult>(userResult.Error);
        }

        var user = userResult.Value;
        await userRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterUserResult(user.Id, user.Email.Value, user.DisplayName);
    }
}
