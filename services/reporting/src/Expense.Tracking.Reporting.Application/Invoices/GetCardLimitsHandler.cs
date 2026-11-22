using Expense.Tracking.Reporting.Application.Abstractions;

namespace Expense.Tracking.Reporting.Application.Invoices;

/// <param name="Outstanding">Falta pagar de todas as faturas do cartao, incluindo parcelas futuras.</param>
/// <param name="AvailableLimit">Limite − falta pagar; negativo quando passou do limite (nada e bloqueado).</param>
public sealed record CardLimitDto(Guid CardId, decimal CreditLimit, decimal Outstanding, decimal AvailableLimit);

/// <summary>
/// INV-10 (ADR-0005, decisao 9): limite disponivel de todos os cartoes do usuario, numa chamada so,
/// para a lista de cartoes. Os cartoes vem do accounts com o token do usuario.
/// </summary>
public sealed class GetCardLimitsHandler(
    ICardDirectory cards, IProjectionQueries projection, InvoiceOptions options, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<CardLimitDto>> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var today = CardInvoiceSource.Today(timeProvider, options);
        var limits = new List<CardLimitDto>();
        foreach (var (card, _) in await cards.ListWithScopesAsync(cancellationToken))
        {
            var loaded = await CardInvoiceSource.LoadAsync(projection, ownerUserId, card, today, cancellationToken);
            limits.Add(new CardLimitDto(card.Id, card.CreditLimit, loaded.Outstanding, card.CreditLimit - loaded.Outstanding));
        }

        return limits;
    }
}
