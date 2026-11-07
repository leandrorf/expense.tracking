using Expense.Tracking.Identity.Application.Abstractions;
using Expense.Tracking.Identity.Application.Authentication;
using Expense.Tracking.Identity.Application.Authorization;
using Expense.Tracking.Identity.Application.Users;
using Expense.Tracking.Identity.Infrastructure.Persistence;
using Expense.Tracking.Identity.Infrastructure.Persistence.Repositories;
using Expense.Tracking.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Identity.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra persistencia, seguranca, opcoes e casos de uso do Identity.</summary>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityOptions(configuration);
        services.AddIdentityPersistence(configuration);
        services.AddIdentitySecurity();
        services.AddIdentityUseCases();
        services.TryAddTimeProvider();
        return services;
    }

    public static IServiceCollection AddIdentityOptions(this IServiceCollection services, IConfiguration configuration)
    {
        var oauthOptions = configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
        services.AddSingleton(oauthOptions);

        var signingOptions = configuration.GetSection(OAuthSigningOptions.SectionName).Get<OAuthSigningOptions>() ?? new OAuthSigningOptions();
        services.AddSingleton(signingOptions);

        return services;
    }

    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("IdentityDb")
            ?? throw new InvalidOperationException("Connection string 'IdentityDb' nao configurada.");

        services.AddDbContext<IdentityDbContext>(options => options.UseMySQL(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IAuthorizationCodeRepository, AuthorizationCodeRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    public static IServiceCollection AddIdentitySecurity(this IServiceCollection services)
    {
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IPkceService, PkceService>();
        services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        return services;
    }

    public static IServiceCollection AddIdentityUseCases(this IServiceCollection services)
    {
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<AuthenticateUserHandler>();
        services.AddScoped<IssueAuthorizationCodeHandler>();
        services.AddScoped<AuthorizationCodeGrantHandler>();
        services.AddScoped<RefreshTokenGrantHandler>();
        services.AddScoped<ClientCredentialsGrantHandler>();
        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
