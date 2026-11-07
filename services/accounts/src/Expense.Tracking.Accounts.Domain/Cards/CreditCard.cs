using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Domain.Cards;

/// <summary>
/// Cartao de credito vinculado a uma conta. Possui ciclo de cobranca (dia de fechamento
/// e dia de vencimento) e limite. Pertence ao mesmo dono da conta.
/// </summary>
public sealed class CreditCard
{
    public const int LabelMaxLength = 80;

    private CreditCard(
        Guid id, Guid ownerUserId, Guid accountId, string label, string brand,
        string lastFourDigits, decimal creditLimit, int closingDay, int dueDay,
        bool isActive, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        AccountId = accountId;
        Label = label;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        CreditLimit = creditLimit;
        ClosingDay = closingDay;
        DueDay = dueDay;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid AccountId { get; private set; }

    public string Label { get; private set; }

    public string Brand { get; private set; }

    public string LastFourDigits { get; private set; }

    public decimal CreditLimit { get; private set; }

    public int ClosingDay { get; private set; }

    public int DueDay { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<CreditCard> Create(
        Guid ownerUserId, Guid accountId, string? label, string? brand,
        string? lastFourDigits, decimal creditLimit, int closingDay, int dueDay,
        DateTimeOffset nowUtc)
    {
        if (ownerUserId == Guid.Empty || accountId == Guid.Empty)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.owner_account_required", "Dono e conta sao obrigatorios."));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return Result.Failure<CreditCard>(Error.Validation("card.label_required", "Apelido do cartao e obrigatorio."));
        }

        var trimmedLabel = label.Trim();
        if (trimmedLabel.Length > LabelMaxLength)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.label_too_long", $"Apelido deve ter no maximo {LabelMaxLength} caracteres."));
        }

        var digits = new string((lastFourDigits ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 4)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.last_four_invalid", "Os ultimos 4 digitos sao obrigatorios (4 numeros)."));
        }

        if (creditLimit < 0)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.limit_negative", "O limite nao pode ser negativo."));
        }

        if (!IsValidDay(closingDay) || !IsValidDay(dueDay))
        {
            return Result.Failure<CreditCard>(Error.Validation("card.day_invalid", "Dias de fechamento/vencimento devem estar entre 1 e 28."));
        }

        return new CreditCard(
            Guid.NewGuid(), ownerUserId, accountId, trimmedLabel,
            string.IsNullOrWhiteSpace(brand) ? "Outro" : brand.Trim(),
            digits, creditLimit, closingDay, dueDay,
            isActive: true, createdAtUtc: nowUtc, updatedAtUtc: nowUtc);
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    // Limitamos a 28 para evitar meses sem o dia (ex.: 29-31 em fevereiro).
    private static bool IsValidDay(int day) => day is >= 1 and <= 28;
}
