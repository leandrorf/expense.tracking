using Expense.Tracking.Accounts.Domain.Common;

namespace Expense.Tracking.Accounts.Domain.Cards;

/// <summary>
/// Cartao de credito vinculado a uma conta. Possui ciclo de cobranca (dia de fechamento
/// e dia de vencimento) e limite. Pertence ao mesmo dono da conta.
/// </summary>
public sealed class CreditCard
{
    public const int LabelMaxLength = 80;

    // CYC-3 (ADR-0012, decisao 1): trocas de ciclo depois do da criacao (que fica em ClosingDay/DueDay).
    private readonly List<CardCycleChange> _cycleChanges = [];

    private CreditCard(
        Guid id, Guid ownerUserId, Guid accountId, string label, string brand,
        string lastFourDigits, decimal creditLimit, int closingDay, int dueDay,
        bool isActive, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        AccountId = accountId;
        Label = label;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        CreditLimit = creditLimit;
        ClosingDay = closingDay;
        DueDay = dueDay;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid AccountId { get; private set; }

    public string Label { get; private set; }

    public string Brand { get; private set; }

    public string LastFourDigits { get; private set; }

    public decimal CreditLimit { get; private set; }

    /// <summary>Dia de fechamento do ciclo da criacao (vale desde sempre). O de hoje vem de <see cref="CurrentCycle"/>.</summary>
    public int ClosingDay { get; private set; }

    /// <summary>Dia de vencimento do ciclo da criacao.</summary>
    public int DueDay { get; private set; }

    /// <summary>CYC-3: todos os ciclos, o da criacao primeiro (com <c>FromDueMonth</c> nulo).</summary>
    public CardCycleSchedule Cycles => new(
        [new CardCycleTerm(null, ClosingDay, DueDay), .. _cycleChanges.Select(c => new CardCycleTerm(c.FromDueMonth, c.ClosingDay, c.DueDay))]);

    /// <summary>CYC-3: o ciclo da fatura que esta aberta em <paramref name="today"/>.</summary>
    public CardCycleTerm CurrentCycle(DateOnly today)
    {
        var cycles = Cycles;
        return cycles.TermOf(cycles.DueMonthOf(today));
    }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<CreditCard> Create(
        Guid ownerUserId, Guid accountId, string? label, string? brand,
        string? lastFourDigits, decimal creditLimit, int closingDay, int dueDay,
        DateTimeOffset nowUtc)
    {
        if (ownerUserId == Guid.Empty || accountId == Guid.Empty)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.owner_account_required", "Dono e conta sao obrigatorios."));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return Result.Failure<CreditCard>(Error.Validation("card.label_required", "Apelido do cartao e obrigatorio."));
        }

        var trimmedLabel = label.Trim();
        if (trimmedLabel.Length > LabelMaxLength)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.label_too_long", $"Apelido deve ter no maximo {LabelMaxLength} caracteres."));
        }

        var digits = new string((lastFourDigits ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 4)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.last_four_invalid", "Os ultimos 4 digitos sao obrigatorios (4 numeros)."));
        }

        if (creditLimit < 0)
        {
            return Result.Failure<CreditCard>(Error.Validation("card.limit_negative", "O limite nao pode ser negativo."));
        }

        if (!IsValidDay(closingDay) || !IsValidDay(dueDay))
        {
            return Result.Failure<CreditCard>(Error.Validation("card.day_invalid", "Dias de fechamento/vencimento devem estar entre 1 e 28."));
        }

        return new CreditCard(
            Guid.NewGuid(), ownerUserId, accountId, trimmedLabel,
            string.IsNullOrWhiteSpace(brand) ? "Outro" : brand.Trim(),
            digits, creditLimit, closingDay, dueDay,
            isActive: true, createdAtUtc: nowUtc, updatedAtUtc: nowUtc);
    }

    /// <summary>
    /// CYC-3 (ADR-0012, decisoes 3 e 4): o ciclo passa a <paramref name="closingDay"/>/<paramref name="dueDay"/> a
    /// partir da fatura que vence em <paramref name="fromDueMonth"/>. Substitui as trocas pendentes (fatura ainda
    /// nao comecou). A fatura de <paramref name="fromDueMonth"/> nao pode ter fechado (nem pelo ciclo de antes nem
    /// pelo novo) e o periodo dela nao pode ficar vazio; fora disso, a mensagem diz a proxima fatura possivel.
    /// </summary>
    public Result ChangeCycle(int closingDay, int dueDay, DateOnly fromDueMonth, DateOnly today, DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(Inactive);
        }

        if (!IsValidDay(closingDay) || !IsValidDay(dueDay))
        {
            return Result.Failure(DayInvalid);
        }

        var month = CardCycleSchedule.FirstDayOf(fromDueMonth);
        var started = _cycleChanges.Where(c => !IsPending(c, today)).ToList();
        if (!CanStartAt(started, closingDay, dueDay, month, today))
        {
            var next = Enumerable.Range(1, 3).Select(i => month.AddMonths(i))
                .Where(m => m > month)
                .FirstOrDefault(m => CanStartAt(started, closingDay, dueDay, m, today));
            var hint = next == default
                ? "escolha uma fatura que ainda nao fechou"
                : $"a proxima fatura possivel e a que vence em {next:MM/yyyy}";
            return Result.Failure(Error.Validation(
                "card.cycle_change_invalid",
                $"O ciclo novo nao pode comecar na fatura que vence em {month:MM/yyyy}: ela ja fechou; {hint}."));
        }

        var replaced = _cycleChanges.Where(c => IsPending(c, today) || c.FromDueMonth >= month).ToList();
        foreach (var change in replaced)
        {
            _cycleChanges.Remove(change);
        }
        _cycleChanges.Add(new CardCycleChange(month, closingDay, dueDay));
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>CYC-3 (ADR-0012, decisao 4): desfaz uma troca que ainda nao comecou a valer.</summary>
    public Result RemoveCycleChange(DateOnly fromDueMonth, DateOnly today, DateTimeOffset nowUtc)
    {
        var month = CardCycleSchedule.FirstDayOf(fromDueMonth);
        if (_cycleChanges.FirstOrDefault(c => c.FromDueMonth == month) is not { } change)
        {
            return Result.Failure(Error.NotFound("card.cycle_not_found", "Nao ha troca de ciclo a partir dessa fatura."));
        }

        if (!IsPending(change, today))
        {
            return Result.Failure(Error.Validation(
                "card.cycle_already_started", "Essa troca de ciclo ja esta valendo: para mudar de novo, faca outra troca."));
        }

        _cycleChanges.Remove(change);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// CYC-2 (ADR-0012, decisao 7): corrige nome, bandeira, final e limite com as regras da criacao. A conta e o
    /// ciclo nao mudam aqui; cartao desativado nao e editado.
    /// </summary>
    public Result Edit(string? label, string? brand, string? lastFourDigits, decimal creditLimit, DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(Inactive);
        }

        var valid = Create(OwnerUserId, AccountId, label, brand, lastFourDigits, creditLimit, ClosingDay, DueDay, nowUtc);
        if (valid.IsFailure)
        {
            return Result.Failure(valid.Error);
        }

        Label = valid.Value.Label;
        Brand = valid.Value.Brand;
        LastFourDigits = valid.Value.LastFourDigits;
        CreditLimit = valid.Value.CreditLimit;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public static readonly Error Inactive = Error.Validation("card.inactive", "Cartao desativado nao pode ser alterado.");

    private static readonly Error DayInvalid =
        Error.Validation("card.day_invalid", "Dias de fechamento/vencimento devem estar entre 1 e 28.");

    /// <summary>Pendente = a fatura em que a troca comeca ainda nao abriu (o inicio dela e depois de hoje).</summary>
    private bool IsPending(CardCycleChange change, DateOnly today) => Cycles.PeriodOf(change.FromDueMonth).StartDate > today;

    /// <summary>
    /// A troca pode comecar em <paramref name="month"/> (ADR-0012, decisao 3): nenhuma troca ja comecada e de
    /// <paramref name="month"/> em diante, o primeiro fechamento novo e hoje ou depois e a fatura de
    /// <paramref name="month"/> pelo ciclo de antes (so com as trocas ja comecadas), se existe, nao fechou.
    /// </summary>
    private bool CanStartAt(IReadOnlyList<CardCycleChange> started, int closingDay, int dueDay, DateOnly month, DateOnly today)
    {
        if (started.Any(c => c.FromDueMonth >= month))
        {
            return false;
        }

        var before = new CardCycleSchedule(
            [new CardCycleTerm(null, ClosingDay, DueDay), .. started.Select(c => new CardCycleTerm(c.FromDueMonth, c.ClosingDay, c.DueDay))]);
        return new CardCycleTerm(month, closingDay, dueDay).ClosingDateOf(month) >= today
            && (!before.Exists(month) || before.ClosingDateOf(month) >= today);
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    // Limitamos a 28 para evitar meses sem o dia (ex.: 29-31 em fevereiro).
    private static bool IsValidDay(int day) => day is >= 1 and <= 28;
}

/// <summary>CYC-3 (ADR-0012, decisao 1): uma troca de ciclo, valendo a partir da fatura que vence em <see cref="FromDueMonth"/>.</summary>
public sealed class CardCycleChange
{
    public CardCycleChange(DateOnly fromDueMonth, int closingDay, int dueDay)
    {
        FromDueMonth = fromDueMonth;
        ClosingDay = closingDay;
        DueDay = dueDay;
    }

    /// <summary>Mes de vencimento (dia 1) da primeira fatura do ciclo novo.</summary>
    public DateOnly FromDueMonth { get; private set; }

    public int ClosingDay { get; private set; }

    public int DueDay { get; private set; }
}
