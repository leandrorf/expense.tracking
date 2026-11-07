using Expense.Tracking.Identity.Domain.Common;

namespace Expense.Tracking.Identity.Domain.Users;

/// <summary>
/// Agregado de usuario do Identity. Dono das credenciais e dos dados de perfil.
/// </summary>
public sealed class User
{
    // Construtor usado pelo EF Core na materializacao.
    private User(Guid id, Email email, PasswordHash passwordHash, string displayName, bool emailConfirmed, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        EmailConfirmed = emailConfirmed;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Email Email { get; private set; }

    public PasswordHash PasswordHash { get; private set; }

    public string DisplayName { get; private set; }

    public bool EmailConfirmed { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<User> Register(Email email, PasswordHash passwordHash, string? displayName, DateTimeOffset nowUtc)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? email.Value : displayName.Trim();

        var user = new User(
            id: Guid.NewGuid(),
            email: email,
            passwordHash: passwordHash,
            displayName: name,
            emailConfirmed: false,
            createdAtUtc: nowUtc,
            updatedAtUtc: nowUtc);

        return user;
    }

    public void ChangePassword(PasswordHash newHash, DateTimeOffset nowUtc)
    {
        PasswordHash = newHash;
        UpdatedAtUtc = nowUtc;
    }

    public void ConfirmEmail(DateTimeOffset nowUtc)
    {
        EmailConfirmed = true;
        UpdatedAtUtc = nowUtc;
    }
}
