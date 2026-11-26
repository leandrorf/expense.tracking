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
        services.AddScoped<SetOpeningBalanceHandler>();
        services.AddScoped<ClearOpeningBalanceHandler>();
        services.AddScoped<DeactivateAccountHandler>();
        services.AddScoped<IAccountScopeLookup, AccountScopeLookup>();
        services.AddScoped<GetAccountScopesHandler>();
        services.AddScoped<GetOwnerCardsForServiceHandler>();
        services.AddScoped<CreateCreditCardHandler>();
        services.AddScoped<ListCreditCardsHandler>();
        services.AddScoped<GetCreditCardHandler>();
        services.AddScoped<DeactivateCreditCardHandler>();
        // CYC-3 (ADR-0012): troca de ciclo com vigencia; "hoje" no fuso do app.
        services.AddSingleton(AppCalendar.FromId(configuration["TimeZone"]));
        services.AddScoped<ChangeCreditCardCycleHandler>();
        services.AddScoped<RemoveCreditCardCycleHandler>();

        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}
