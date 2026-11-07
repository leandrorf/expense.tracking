namespace Expense.Tracking.Accounts.Domain.Payments;

/// <summary>Meios de pagamento suportados para lancamentos de gastos.</summary>
public enum PaymentMethod
{
    Cash = 0,         // dinheiro
    Pix = 1,
    Boleto = 2,
    CreditCard = 3,   // cartao de credito
    DebitCard = 4,    // cartao de debito
    BankTransfer = 5  // transferencia (TED/DOC)
}

public sealed record PaymentMethodInfo(PaymentMethod Method, string Code, string DisplayName, bool RequiresCard);

/// <summary>Catalogo estavel dos meios de pagamento, exposto pela API.</summary>
public static class PaymentMethodCatalog
{
    public static readonly IReadOnlyList<PaymentMethodInfo> All =
    [
        new(PaymentMethod.Cash, "cash", "Dinheiro", RequiresCard: false),
        new(PaymentMethod.Pix, "pix", "Pix", RequiresCard: false),
        new(PaymentMethod.Boleto, "boleto", "Boleto", RequiresCard: false),
        new(PaymentMethod.CreditCard, "credit_card", "Cartao de credito", RequiresCard: true),
        new(PaymentMethod.DebitCard, "debit_card", "Cartao de debito", RequiresCard: true),
        new(PaymentMethod.BankTransfer, "bank_transfer", "Transferencia bancaria", RequiresCard: false)
    ];
}
