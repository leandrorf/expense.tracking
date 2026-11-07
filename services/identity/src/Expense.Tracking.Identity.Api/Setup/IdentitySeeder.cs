using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Domain.Clients;
using Expense.Tracking.Identity.Domain.Users;
using Expense.Tracking.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Expense.Tracking.Identity.Api.Setup;

/// <summary>
/// Semeia dados de desenvolvimento: um cliente SPA publico, um cliente de servico
/// confidencial e um usuario demo. Idempotente.
/// </summary>
public static class IdentitySeeder
{
    public const string SpaClientId = "expense-tracking-spa";
    public const string ServiceClientId = "expense-tracking-service";
    public const string DemoUserEmail = "demo@expense.local";
    public const string DemoUserPassword = "Password123!";
    public const string ServiceClientSecret = "dev-service-secret";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = timeProvider.GetUtcNow();

        await SeedSpaClientAsync(db, now, cancellationToken);
        await SeedServiceClientAsync(db, passwordHasher, now, cancellationToken);
        await SeedDemoUserAsync(db, passwordHasher, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSpaClientAsync(IdentityDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Clients.AnyAsync(c => c.ClientId == SpaClientId, ct))
        {
            return;
        }

        var spa = OAuthClient.Create(
            clientId: SpaClientId,
            displayName: "Expense Tracking SPA",
            type: ClientType.Public,
            clientSecretHash: null,
            redirectUris: ["http://localhost:5173/callback", "http://localhost:5173/silent-renew"],
            allowedScopes: ["openid", "profile", "offline_access", "expenses.read", "expenses.write"],
            allowedGrantTypes: [GrantType.AuthorizationCode, GrantType.RefreshToken],
            nowUtc: now,
            requirePkce: true);

        db.Clients.Add(spa);
    }

    private static async Task SeedServiceClientAsync(IdentityDbContext db, IPasswordHasher hasher, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Clients.AnyAsync(c => c.ClientId == ServiceClientId, ct))
        {
            return;
        }

        var service = OAuthClient.Create(
            clientId: ServiceClientId,
            displayName: "Expense Tracking Service (M2M)",
            type: ClientType.Confidential,
            clientSecretHash: hasher.Hash(ServiceClientSecret),
            redirectUris: [],
            allowedScopes: ["expenses.read", "expenses.write"],
            allowedGrantTypes: [GrantType.ClientCredentials],
            nowUtc: now,
            requirePkce: false);

        db.Clients.Add(service);
    }

    private static async Task SeedDemoUserAsync(IdentityDbContext db, IPasswordHasher hasher, DateTimeOffset now, CancellationToken ct)
    {
        var email = Email.Create(DemoUserEmail);
        if (email.IsFailure || await db.Users.AnyAsync(u => u.Email == email.Value, ct))
        {
            return;
        }

        var passwordHash = PasswordHash.Create(hasher.Hash(DemoUserPassword));
        var user = User.Register(email.Value, passwordHash.Value, "Demo User", now);
        if (user.IsSuccess)
        {
            db.Users.Add(user.Value);
        }
    }
}
