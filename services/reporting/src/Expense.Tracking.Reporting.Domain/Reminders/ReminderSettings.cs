using Expense.Tracking.Reporting.Domain.Common;

namespace Expense.Tracking.Reporting.Domain.Reminders;

/// <summary>
/// NTF-6 (ADR-0009, decisao 4): preferencias dos avisos de um usuario. Sem linha gravada, valem os padroes
/// (<see cref="Default"/>): 3 dias de antecedencia e todos os tipos ligados.
/// </summary>
public sealed class ReminderSettings
{
    private ReminderSettings()
    {
    }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Dias corridos de antecedencia (0 = so o que vence hoje).</summary>
    public int LeadDays { get; private set; } = ReminderCalculator.DefaultLeadDays;

    /// <summary>Avisos de fatura (a vencer e vencida).</summary>
    public bool Invoices { get; private set; } = true;

    /// <summary>Avisos de recorrencia (a vencer e "a lancar").</summary>
    public bool Recurrences { get; private set; } = true;

    /// <summary>NTF-9: avisos de orcamento em Atencao ou Estourado.</summary>
    public bool Budgets { get; private set; } = true;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // ---------- NTF-8 (ADR-0009, decisao 6): resumo diario por e-mail ----------

    public const int EmailMaxLength = 254;
    public const int DefaultSendHour = 8;

    /// <summary>Validade do link de confirmacao do endereco.</summary>
    public static readonly TimeSpan EmailTokenLifetime = TimeSpan.FromHours(24);

    /// <summary>O usuario quer o resumo diario (so sai com o endereco confirmado).</summary>
    public bool EmailEnabled { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Quando o endereco atual foi confirmado; nulo = ainda nao (nada e enviado).</summary>
    public DateTimeOffset? EmailConfirmedAtUtc { get; private set; }

    /// <summary>Hash (SHA-256, hex) do token de confirmacao pendente; o token em si nunca e guardado.</summary>
    public string? EmailTokenHash { get; private set; }

    public DateTimeOffset? EmailTokenExpiresAtUtc { get; private set; }

    /// <summary>Hora local (0 a 23) do resumo.</summary>
    public int SendHour { get; private set; } = DefaultSendHour;

    /// <summary>Dia (local) do ultimo resumo enviado.</summary>
    public DateOnly? LastSentOn { get; private set; }

    /// <summary>Chaves dos avisos do ultimo resumo, separadas por quebra de linha: so aviso novo gera outro envio.</summary>
    public string? LastSentKeys { get; private set; }

    public bool EmailConfirmed => Email is not null && EmailConfirmedAtUtc is not null;

    public static ReminderSettings Default(Guid ownerUserId) => new() { OwnerUserId = ownerUserId };

    /// <summary>
    /// Endereco, liga/desliga e hora do resumo. Trocar o endereco zera a confirmacao e guarda o hash do token
    /// novo (<paramref name="newTokenHash"/>); devolve <c>true</c> quando o endereco mudou e a confirmacao
    /// precisa ser enviada.
    /// </summary>
    public Result<bool> UpdateEmail(string? email, bool enabled, int sendHour, string newTokenHash, DateTimeOffset nowUtc)
    {
        if (sendHour is < 0 or > 23)
        {
            return Result.Failure<bool>(Error.Validation("reminders.send_hour_invalid", "A hora do resumo vai de 0 a 23."));
        }

        var normalized = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (normalized is not null && !LooksLikeEmail(normalized))
        {
            return Result.Failure<bool>(Error.Validation("reminders.email_invalid", "Informe um e-mail valido."));
        }

        if (enabled && normalized is null)
        {
            return Result.Failure<bool>(Error.Validation("reminders.email_required", "Informe o e-mail para receber o resumo."));
        }

        var changed = normalized != Email;
        if (changed)
        {
            Email = normalized;
            EmailConfirmedAtUtc = null;
            EmailTokenHash = normalized is null ? null : newTokenHash;
            EmailTokenExpiresAtUtc = normalized is null ? null : nowUtc + EmailTokenLifetime;
            // O endereco novo ainda nao recebeu nada: o resumo de hoje pode sair para ele.
            LastSentOn = null;
            LastSentKeys = null;
        }

        EmailEnabled = enabled;
        SendHour = sendHour;
        UpdatedAtUtc = nowUtc;
        return changed && normalized is not null;
    }

    /// <summary>Gera de novo o token de confirmacao do endereco atual (link perdido ou vencido).</summary>
    public Result ResendConfirmation(string newTokenHash, DateTimeOffset nowUtc)
    {
        if (Email is null || EmailConfirmedAtUtc is not null)
        {
            return Result.Failure(Error.Validation("reminders.email_not_pending", "Nao ha e-mail esperando confirmacao."));
        }

        EmailTokenHash = newTokenHash;
        EmailTokenExpiresAtUtc = nowUtc + EmailTokenLifetime;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Confirma o endereco com o token do link: uso unico e dentro da validade.</summary>
    public Result ConfirmEmail(string tokenHash, DateTimeOffset nowUtc)
    {
        if (EmailTokenHash is null || EmailTokenExpiresAtUtc is null || nowUtc > EmailTokenExpiresAtUtc
            || !string.Equals(EmailTokenHash, tokenHash, StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation(
                "reminders.email_token_invalid", "Link de confirmacao invalido ou vencido: peca um novo nas preferencias."));
        }

        EmailConfirmedAtUtc = nowUtc;
        EmailTokenHash = null;
        EmailTokenExpiresAtUtc = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>O resumo deve sair agora? Ligado, confirmado, na hora escolhida e ainda nao enviado hoje.</summary>
    public bool IsDigestDue(DateOnly today, int localHour) =>
        EmailEnabled && EmailConfirmed && localHour == SendHour && LastSentOn != today;

    /// <summary>Ha aviso que nao estava no ultimo resumo?</summary>
    public bool HasNewKeys(IEnumerable<string> keys)
    {
        var previous = (LastSentKeys ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return keys.Any(k => !previous.Contains(k));
    }

    public void RecordDigestSent(DateOnly today, IEnumerable<string> keys, DateTimeOffset nowUtc)
    {
        LastSentOn = today;
        LastSentKeys = string.Join('\n', keys);
        UpdatedAtUtc = nowUtc;
    }

    private static bool LooksLikeEmail(string value)
    {
        if (value.Length > EmailMaxLength || value.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.');
    }

    public Result Update(int leadDays, bool invoices, bool recurrences, bool budgets, DateTimeOffset nowUtc)
    {
        if (leadDays is < 0 or > ReminderCalculator.MaxLeadDays)
        {
            return Result.Failure(Error.Validation(
                "reminders.lead_days_invalid", $"A antecedencia vai de 0 a {ReminderCalculator.MaxLeadDays} dias."));
        }

        LeadDays = leadDays;
        Invoices = invoices;
        Recurrences = recurrences;
        Budgets = budgets;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
