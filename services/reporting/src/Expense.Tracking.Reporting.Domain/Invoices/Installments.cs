namespace Expense.Tracking.Reporting.Domain.Invoices;

/// <summary>
/// INV-3 (ADR-0005, decisao 5): divide o valor de uma compra parcelada. A parcela e o valor
/// dividido pelo numero de parcelas, arredondado para baixo em centavos; a diferenca vai na
/// primeira. R$ 100,00 em 3x = 33,34 + 33,33 + 33,33. A soma sempre fecha com o valor lancado.
/// </summary>
public static class Installments
{
    public const int Max = 24;

    public static IReadOnlyList<decimal> Split(decimal amount, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Max);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var installment = Math.Floor(amount * 100m / count) / 100m;
        var first = amount - (installment * (count - 1));
        return [first, .. Enumerable.Repeat(installment, count - 1)];
    }
}
