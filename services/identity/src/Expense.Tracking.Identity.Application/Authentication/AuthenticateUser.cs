using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Common;
using Expense.Tracking.Identity.Domain.Users;

namespace Expense.Tracking.Identity.Application.Authentication;

public sealed record AuthenticateUserCommand(string Email, string Password);

public sealed record AuthenticatedUser(Guid UserId, string Email, string DisplayName);

/// <summary>
/// Valida credenciais de usuario (login). Mensagens genericas para nao permitir
/// enumeracao de contas.
/// </summary>
public sealed class AuthenticateUserHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "E-mail ou senha invalidos.");

    public async Task<Result<AuthenticatedUser>> HandleAsync(AuthenticateUserCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<AuthenticatedUser>(InvalidCredentials);
        }

        var user = await userRepository.GetByEmailAsync(emailResult.Value.Value, cancellationToken);
        if (user is null)
        {
            // Verificacao "dummy" para reduzir diferenca de tempo entre usuario inexistente e senha errada.
            passwordHasher.Verify(command.Password ?? string.Empty, DummyHash);
            return Result.Failure<AuthenticatedUser>(InvalidCredentials);
        }

        return passwordHasher.Verify(command.Password ?? string.Empty, user.PasswordHash.Value)
            ? new AuthenticatedUser(user.Id, user.Email.Value, user.DisplayName)
            : Result.Failure<AuthenticatedUser>(InvalidCredentials);
    }

    // Hash PBKDF2 valido (senha: "invalid") apenas para gastar tempo equivalente numa conta inexistente.
    private const string DummyHash =
        "pbkdf2-sha256$100000$AAAAAAAAAAAAAAAAAAAAAA==$2S8n1b8b0mq0m1b8b0mq0m1b8b0mq0m1b8b0mq0m1b8=";
}
