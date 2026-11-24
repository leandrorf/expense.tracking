using Expense.Tracking.Reporting.Domain.Common;
using Expense.Tracking.Reporting.Domain.Reminders;

namespace Expense.Tracking.Reporting.Application.Reminders;

/// <summary>NTF-6: preferencias dos avisos como a API as mostra e recebe.</summary>
public sealed record ReminderSettingsDto(int LeadDays, bool Invoices, bool Recurrences, bool Budgets)
{
    public static ReminderSettingsDto From(ReminderSettings s) => new(s.LeadDays, s.Invoices, s.Recurrences, s.Budgets);
}

public sealed class GetReminderSettingsHandler(IReminderStore store)
{
    public async Task<ReminderSettingsDto> HandleAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        ReminderSettingsDto.From(await store.FindSettingsAsync(ownerUserId, cancellationToken) ?? ReminderSettings.Default(ownerUserId));
}

/// <summary>
/// NTF-6 (ADR-0009, decisao 4): grava as preferencias (cria a linha na primeira vez). Antecedencia de 0 a
/// 15 dias; cada tipo de aviso pode ser desligado.
/// </summary>
public sealed class UpdateReminderSettingsHandler(IReminderStore store, TimeProvider timeProvider)
{
    public async Task<Result<ReminderSettingsDto>> HandleAsync(
        Guid ownerUserId, ReminderSettingsDto input, CancellationToken cancellationToken)
    {
        var settings = await store.FindSettingsAsync(ownerUserId, cancellationToken);
        var isNew = settings is null;
        settings ??= ReminderSettings.Default(ownerUserId);

        var updated = settings.Update(input.LeadDays, input.Invoices, input.Recurrences, input.Budgets, timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return Result.Failure<ReminderSettingsDto>(updated.Error);
        }

        if (isNew)
        {
            store.AddSettings(settings);
        }

        await store.SaveChangesAsync(cancellationToken);
        return ReminderSettingsDto.From(settings);
    }
}
