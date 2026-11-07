namespace Expense.Tracking.Transactions.Domain.Transactions;

/// <summary>Meio de pagamento do lancamento (espelha o catalogo do servico accounts).</summary>
public enum PaymentMethod
{
    Cash = 0,
    Pix = 1,
    Boleto = 2,
    CreditCard = 3,
    DebitCard = 4,
    BankTransfer = 5
}

/// <summary>Tipo do lancamento: gasto (saida) ou receita (entrada).</summary>
public enum TransactionType
{
    Expense = 0,
    Income = 1
}
