using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Users;
using Expense.Tracking.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Api.Setup;

/// <summary>
/// Semeia os clientes OAuth e os usuarios declarados em <see cref="SeedOptions"/> (IDX-2). Idempotente:
/// cliente existente so ganha os escopos novos da configuracao (bancos antigos recebem escopo novo
/// sem recriar o cliente); usuario existente fica como esta.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, SeedOptions options, CancellationToken cancellationToken = default)
    {
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Configuracao de seed invalida:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        foreach (var (clientId, client) in options.Clients)
        {
            await SeedClientAsync(db, passwordHasher, clientId, client, now, cancellationToken);
        }

        foreach (var (name, user) in options.Users)
        {
            await SeedUserAsync(db, passwordHasher, name, user, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedClientAsync(
        IdentityDbContext db, IPasswordHasher hasher, string clientId, SeedClientOptions options, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Clients.FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
        if (existing is not null)
        {
            existing.AllowScopes(options.Scopes);
            return;
        }

        db.Clients.Add(OAuthClient.Create(
            clientId: clientId,
            displayName: string.IsNullOrWhiteSpace(options.DisplayName) ? clientId : options.DisplayName,
            type: options.Type,
            clientSecretHash: options.Type == ClientType.Confidential ? hasher.Hash(options.Secret!) : null,
            redirectUris: options.RedirectUris,
            allowedScopes: options.Scopes,
            allowedGrantTypes: options.GrantTypes,
            nowUtc: now,
            requirePkce: options.RequirePkce));
    }

    private static async Task SeedUserAsync(
        IdentityDbContext db, IPasswordHasher hasher, string name, SeedUserOptions options, DateTimeOffset now, CancellationToken ct)
    {
        var email = Email.Create(options.Email!);
        if (email.IsFailure)
        {
            throw new InvalidOperationException($"Configuracao de seed invalida: {SeedOptions.SectionName}:Users:{name}:Email: {email.Error.Description}");
        }

        if (await db.Users.AnyAsync(u => u.Email == email.Value, ct))
        {
            return;
        }

        var passwordHash = PasswordHash.Create(hasher.Hash(options.Password!));
        var user = User.Register(email.Value, passwordHash.Value, string.IsNullOrWhiteSpace(options.DisplayName) ? name : options.DisplayName, now);
        if (user.IsFailure)
        {
            throw new InvalidOperationException($"Configuracao de seed invalida: {SeedOptions.SectionName}:Users:{name}: {user.Error.Description}");
        }

        db.Users.Add(user.Value);
    }
}
