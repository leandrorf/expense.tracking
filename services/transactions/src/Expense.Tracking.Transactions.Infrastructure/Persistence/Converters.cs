using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Expense.Tracking.Transactions.Infrastructure.Persistence;

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

/// <summary>Converte DateOnly &lt;-&gt; DateTime (portavel entre provedores).</summary>
public sealed class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyConverter()
        : base(
            d => d.ToDateTime(TimeOnly.MinValue),
            dt => DateOnly.FromDateTime(dt))
    {
    }
}
