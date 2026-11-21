using Expense.Tracking.Reporting.Application.Abstractions;
using Expense.Tracking.Reporting.Application.Invoices;
using Expense.Tracking.Reporting.Domain.Budgets;
using Expense.Tracking.Reporting.Domain.Reports;

namespace Expense.Tracking.Reporting.UnitTests;

/// <summary>INV-4: o "hoje" das faturas segue o fuso configurado, nao o UTC do servidor.</summary>
public sealed class GetCardInvoicesHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly CardInfo Card = new(Guid.NewGuid(), Guid.NewGuid(), "Cartao", "4242", 5000m, 10, 17, true);

    // Fuso fixo em -3h, como Sao Paulo hoje: o teste nao depende do banco de fusos da maquina.
    private static readonly TimeZoneInfo Brasilia =
        TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasilia", "Brasilia");

    [Theory]
    // 22h de 10/12 em Sao Paulo = 01h de 11/12 em UTC: ainda e o dia do fechamento, fatura aberta.
    [InlineData("2026-12-11T01:00:00Z", true, "Open")]
    // 22h de 10/12 em UTC: tambem dia do fechamento.
    [InlineData("2026-12-10T22:00:00Z", false, "Open")]
    // 01h de 11/12 em UTC, com o servidor em UTC: ja fechou.
    [InlineData("2026-12-11T01:00:00Z", false, "Closed")]
    public async Task Status_UsesTheConfiguredTimeZone(string utcNow, bool brasilia, string expected)
    {
        var handler = new GetCardInvoicesHandler(
            new OneCard(), new OnePurchase(), new InvoiceOptions(brasilia ? Brasilia : TimeZoneInfo.Utc),
            new FixedTime(DateTimeOffset.Parse(utcNow)));

        var result = await handler.HandleAsync(Owner, Card.Id, null, null, CancellationToken.None);

        var december = Assert.Single(result.Value.Invoices, i => i.DueMonth == "2026-12");
        Assert.Equal(expected, december.Status);
    }

    [Fact]
    public async Task UnknownCard_IsNotFound()
    {
        var handler = new GetCardInvoicesHandler(
            new OneCard(), new OnePurchase(), new InvoiceOptions(TimeZoneInfo.Utc), new FixedTime(DateTimeOffset.UtcNow));

        var result = await handler.HandleAsync(Owner, Guid.NewGuid(), null, null, CancellationToken.None);

        Assert.Equal("card.not_found", result.Error.Code);
    }

    private sealed class OneCard : ICardDirectory
    {
        public Task<CardInfo?> FindAsync(Guid cardId, CancellationToken cancellationToken) =>
            Task.FromResult(cardId == Card.Id ? Card : null);
    }

    /// <summary>Uma compra em 01/12, na fatura que fecha em 10/12 e vence em 17/12.</summary>
    private sealed class OnePurchase : IProjectionQueries
    {
        public Task<IReadOnlyList<CardTransactionRecord>> GetCardTransactionsAsync(Guid ownerUserId, Guid cardId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardTransactionRecord>>(
                [new(Guid.NewGuid(), "Expense", "Mercado", "Mercado", 100m, "BRL", new DateOnly(2026, 12, 1), 1)]);

        public Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(Guid ownerUserId, ReportFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransactionRecord>>([]);

        public Task<IReadOnlyList<CategoryUsage>> GetCategoryUsageAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CategoryUsage>>([]);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
