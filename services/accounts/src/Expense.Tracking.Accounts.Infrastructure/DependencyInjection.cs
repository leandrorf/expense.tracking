using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Application.Accounts;
using Expense.Tracking.Accounts.Application.Cards;
using Expense.Tracking.Accounts.Infrastructure.Persistence;
using Expense.Tracking.Accounts.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Tracking.Accounts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AccountsDb")
            ?? throw new InvalidOperationException("Connection string 'AccountsDb' nao configurada.");

        services.AddDbContext<AccountsDbContext>(options => options.UseMySQL(connectionString));

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ICreditCardRepository, CreditCardRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<CreateAccountHandler>();
        services.AddScoped<ListAccountsHandler>();
        services.AddScoped<GetAccountHandler>();
        services.AddScoped<RenameAccountHandler>();
        services.AddScoped<DeactivateAccountHandler>();
        services.AddScoped<CreateCreditCardHandler>();
        services.AddScoped<ListCreditCardsHandler>();
        services.AddScoped<GetCreditCardHandler>();
        services.AddScoped<DeactivateCreditCardHandler>();

        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}
