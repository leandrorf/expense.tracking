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
