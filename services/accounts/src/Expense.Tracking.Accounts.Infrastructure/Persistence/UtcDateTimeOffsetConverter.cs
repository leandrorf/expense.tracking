using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Expense.Tracking.Accounts.Infrastructure.Persistence;

/// <summary>Converte DateTimeOffset &lt;-&gt; DateTime(UTC) para armazenamento em MySQL.</summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTime>
{
    public UtcDateTimeOffsetConverter()
        : base(
            dto => dto.UtcDateTime,
            dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)))
    {
    }
}

/// <summary>
/// INV-13: converte DateOnly &lt;-&gt; DateTime. O provider Oracle MySQL nao le DateOnly (devolve
/// DateTime e o EF falha no cast); o mesmo conversor que o transactions usa.
/// </summary>
public sealed class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyConverter()
        : base(
            d => d.ToDateTime(TimeOnly.MinValue),
            dt => DateOnly.FromDateTime(dt))
    {
    }
}
