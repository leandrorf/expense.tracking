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

public sealed record PaymentMethodInfo(PaymentMethod Method, string DisplayName, bool RequiresCard)
{
    /// <summary>
    /// Identificador canonico do meio de pagamento: o nome do enum (ex.: "CreditCard").
    /// E o mesmo valor que o servico transactions aceita e devolve, e sobre o qual o
    /// reporting agrupa — contrato unico de ponta a ponta.
    /// </summary>
    public string Code => Method.ToString();
}

/// <summary>Catalogo estavel dos meios de pagamento, exposto pela API.</summary>
public static class PaymentMethodCatalog
{
    public static readonly IReadOnlyList<PaymentMethodInfo> All =
    [
        new(PaymentMethod.Cash, "Dinheiro", RequiresCard: false),
        new(PaymentMethod.Pix, "Pix", RequiresCard: false),
        new(PaymentMethod.Boleto, "Boleto", RequiresCard: false),
        new(PaymentMethod.CreditCard, "Cartao de credito", RequiresCard: true),
        new(PaymentMethod.DebitCard, "Cartao de debito", RequiresCard: true),
        new(PaymentMethod.BankTransfer, "Transferencia bancaria", RequiresCard: false)
    ];
}
