using Expense.Tracking.Transactions.Domain.Transactions;

namespace Expense.Tracking.Transactions.Domain.Imports;

/// <summary>
/// IMP-6 (ADR-0004, decisao 5): pistas no memo de extrato de conta que sugerem o meio de pagamento.
/// So sugerem: o usuario troca na previa. Casa palavra inteira ("PIX", nao "PIXEL"), sem diferenca
/// de maiusculas; com mais de uma pista, vale a primeira no texto.
/// </summary>
public static class PaymentMethodHints
{
    private static readonly IReadOnlyDictionary<string, PaymentMethod> Words =
        new Dictionary<string, PaymentMethod>(StringComparer.OrdinalIgnoreCase)
        {
            ["PIX"] = PaymentMethod.Pix,
            ["BOLETO"] = PaymentMethod.Boleto,
            ["TED"] = PaymentMethod.BankTransfer,
            ["SAQUE"] = PaymentMethod.Cash,
        };

    public static PaymentMethod? Suggest(string? memo)
    {
        if (string.IsNullOrEmpty(memo))
        {
            return null;
        }

        var start = -1;
        for (var i = 0; i <= memo.Length; i++)
        {
            var inWord = i < memo.Length && char.IsLetterOrDigit(memo[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                if (Words.TryGetValue(memo[start..i], out var method))
                {
                    return method;
                }

                start = -1;
            }
        }

        return null;
    }
}
