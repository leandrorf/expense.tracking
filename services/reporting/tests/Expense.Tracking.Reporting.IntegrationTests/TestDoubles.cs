using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Forecast;
using Expense.Tracking.Reporting.Application.Invoices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expense.Tracking.Reporting.IntegrationTests;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubHeader = "X-Test-Sub";
    public const string ScopeHeader = "X-Test-Scope";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Escopo padrao: so leitura. BDG-2: os testes de escrita mandam o escopo pelo header.
        var scope = Request.Headers.TryGetValue(ScopeHeader, out var requested) ? requested.ToString() : "expenses.read";
        var claims = new[] { new Claim("sub", sub.ToString()), new Claim("scope", scope) };
        var identity = new ClaimsIdentity(claims, SchemeName, "sub", roleType: null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>INV-4: cartoes cadastrados no "accounts" dos testes, cada um com seu dono.</summary>
public sealed class CardRegistry
{
    private readonly ConcurrentDictionary<Guid, (Guid Owner, CardInfo Card, string Scope)> _cards = new();
    private readonly ConcurrentDictionary<Guid, (Guid Owner, AccountInfo Account)> _accounts = new();

    /// <param name="cycles">CYC-4: historico de ciclos (o primeiro com <c>FromDueMonth</c> nulo), como o accounts devolve.</param>
    public CardInfo Add(Guid owner, int closingDay, int dueDay, decimal limit = 5000m, string scope = "Personal", Guid? accountId = null,
        IReadOnlyList<CardCycleInfo>? cycles = null)
    {
        var card = new CardInfo(Guid.NewGuid(), accountId ?? Guid.NewGuid(), "Cartao teste", "4242", limit, closingDay, dueDay, IsActive: true,
            cycles);
        _cards[card.Id] = (owner, card, scope);
        return card;
    }

    /// <summary>INV-13: conta do usuario, com ou sem saldo informado.</summary>
    public AccountInfo AddAccount(
        Guid owner, string scope = "Personal", decimal? openingBalance = null, string? openingBalanceOn = null, bool isActive = true)
    {
        var account = new AccountInfo(Guid.NewGuid(), scope, isActive, openingBalance,
            openingBalanceOn is null ? null : DateOnly.Parse(openingBalanceOn, System.Globalization.CultureInfo.InvariantCulture));
        _accounts[account.Id] = (owner, account);
        return account;
    }

    public IReadOnlyList<AccountInfo> AccountsOf(Guid owner) =>
        _accounts.Values.Where(e => e.Owner == owner).Select(e => e.Account).ToList();

    public CardInfo? Find(Guid cardId, Guid owner) =>
        _cards.TryGetValue(cardId, out var entry) && entry.Owner == owner ? entry.Card : null;

    public IReadOnlyList<ScopedCard> ListOf(Guid owner) =>
        _cards.Values.Where(e => e.Owner == owner).Select(e => new ScopedCard(e.Card, e.Scope)).ToList();
}

/// <summary>Faz o papel do accounts: so devolve o cartao ao dono (o accounts filtra pelo token).</summary>
public sealed class FakeCardDirectory(CardRegistry registry, IHttpContextAccessor httpContextAccessor) : ICardDirectory
{
    public Task<CardInfo?> FindAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var sub = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return Task.FromResult(Guid.TryParse(sub, out var owner) ? registry.Find(cardId, owner) : null);
    }

    public Task<IReadOnlyList<ScopedCard>> ListWithScopesAsync(CancellationToken cancellationToken)
    {
        var sub = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return Task.FromResult(Guid.TryParse(sub, out var owner) ? registry.ListOf(owner) : []);
    }

    public Task<IReadOnlyList<AccountInfo>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        var sub = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return Task.FromResult(Guid.TryParse(sub, out var owner) ? registry.AccountsOf(owner) : []);
    }
}

/// <summary>RCN-4: lotes de importacao guardados como o transactions os devolveria, por dono.</summary>
public sealed class ImportBatchRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (Guid Owner, Expense.Tracking.Reporting.Application.Invoices.ImportBatchSnapshot Batch)> _batches = new();

    public Expense.Tracking.Reporting.Application.Invoices.ImportBatchSnapshot Add(
        Guid owner, Expense.Tracking.Reporting.Application.Invoices.ImportBatchSnapshot batch)
    {
        _batches[batch.Id] = (owner, batch);
        return batch;
    }

    public Expense.Tracking.Reporting.Application.Invoices.ImportBatchSnapshot? Find(Guid batchId, Guid owner) =>
        _batches.TryGetValue(batchId, out var entry) && entry.Owner == owner ? entry.Batch : null;
}

/// <summary>Faz o papel do transactions: so devolve o lote ao dono (o transactions filtra pelo token).</summary>
public sealed class FakeImportBatchReader(ImportBatchRegistry registry, IHttpContextAccessor httpContextAccessor)
    : Expense.Tracking.Reporting.Application.Invoices.IImportBatchReader
{
    public Task<Expense.Tracking.Reporting.Application.Invoices.ImportBatchSnapshot?> GetAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var sub = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return Task.FromResult(Guid.TryParse(sub, out var owner) ? registry.Find(batchId, owner) : null);
    }
}

/// <summary>Relogio parado, para a situacao das faturas nao depender do dia em que o teste roda.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>NTF-8: o endpoint interno do accounts nos testes: todos os cartoes do dono, sem token de usuario.</summary>
public sealed class FakeServiceCardDirectory(CardRegistry registry) : Expense.Tracking.Reporting.Application.Reminders.IServiceCardDirectory
{
    public Task<IReadOnlyList<CardInfo>> ListForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CardInfo>>(registry.ListOf(ownerUserId).Select(c => c.Card).ToList());
}

/// <summary>NTF-8: guarda os e-mails em vez de enviar.</summary>
public sealed class RecordingEmailSender : Expense.Tracking.Reporting.Application.Reminders.IEmailSender
{
    private readonly ConcurrentQueue<Expense.Tracking.Reporting.Application.Reminders.EmailMessage> _sent = new();

    public IReadOnlyList<Expense.Tracking.Reporting.Application.Reminders.EmailMessage> Sent => [.. _sent];

    public IReadOnlyList<Expense.Tracking.Reporting.Application.Reminders.EmailMessage> To(string address) =>
        _sent.Where(m => m.To == address).ToList();

    public Task SendAsync(Expense.Tracking.Reporting.Application.Reminders.EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
