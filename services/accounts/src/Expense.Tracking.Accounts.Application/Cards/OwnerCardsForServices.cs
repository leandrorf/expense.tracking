using Expense.Tracking.Accounts.Application.Abstractions;
using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Application.Cards;

/// <summary>
/// NTF-7: cartao como um servico o recebe, com o ciclo e o escopo PF/PJ da conta. Mesmos campos que o
/// reporting le do <c>GET /credit-cards</c> do usuario, mais o escopo.
/// </summary>
public sealed record ServiceCardDto(
    Guid Id,
    Guid AccountId,
    string? AccountScope,
    string Label,
    string LastFourDigits,
    decimal CreditLimit,
    int ClosingDay,
    int DueDay,
    bool IsActive,
    IReadOnlyList<Contracts.CardCycleDto>? Cycles = null);

/// <summary>
/// NTF-7 (ADR-0009, decisao 6): cartoes de um dono (ativos ou nao: um cartao desativado ainda pode ter
/// fatura a vencer), para o job do reporting que calcula avisos sem token de usuario. So atende o
/// endpoint interno com o escopo de servico <c>accounts.cards.read</c>.
/// </summary>
public sealed class GetOwnerCardsForServiceHandler(
    ICreditCardRepository cards, IAccountRepository accounts, AppCalendar calendar, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<ServiceCardDto>>> HandleAsync(Guid? ownerUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId is not { } owner || owner == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<ServiceCardDto>>(Error.Validation(
                "service_cards.owner_required", "Informe o dono em 'ownerUserId'."));
        }

        var scopes = (await accounts.ListAsync(owner, cancellationToken)).ToDictionary(a => a.Id, a => a.Scope.ToString());
        var today = calendar.Today(timeProvider);
        // CYC-3 (ADR-0012, decisao 5): o ciclo de hoje e o historico, como na rota do usuario.
        return (await cards.ListAsync(owner, accountId: null, cancellationToken))
            .Select(c =>
            {
                var current = c.CurrentCycle(today);
                return new ServiceCardDto(c.Id, c.AccountId, scopes.GetValueOrDefault(c.AccountId), c.Label, c.LastFourDigits,
                    c.CreditLimit, current.ClosingDay, current.DueDay, c.IsActive, Contracts.CardCycleDto.ListOf(c));
            })
            .ToList();
    }
}
