import { useEffect, useState } from 'react';
import { ApiError } from '../api/client';
import {
  goalsApi,
  type GoalScope,
  type MonthlyGoal,
  type MonthlyGoalHistoryMonth,
  type MonthlyGoalHistoryScope,
  type MonthlyGoalKind,
  type MonthlyGoalScopeStatus,
  type MonthlyGoalSituation,
  type MonthlyGoalStatus,
  type SavingsObjective,
  type SavingsObjectiveSituation,
} from '../api/goals';

const SCOPES: { scope: GoalScope; label: string; short: string }[] = [
  { scope: 'personal', label: 'Pessoal (PF)', short: 'PF' },
  { scope: 'business', label: 'Empresa (PJ)', short: 'PJ' },
];

const KINDS: { kind: MonthlyGoalKind; label: string }[] = [
  { kind: 'SaveAmount', label: 'Guardar um valor' },
  { kind: 'SavePercent', label: 'Guardar um percentual das entradas' },
  { kind: 'SpendCeiling', label: 'Gastar no máximo (teto)' },
];

/** ADR-0013, regra 5: o mesmo visual de situação dos orçamentos. */
const MONTH_SITUATION: Record<MonthlyGoalSituation, { label: string; icon: string; css: string }> = {
  Inside: { label: 'Dentro da meta', icon: '✓', css: 'ontrack' },
  Attention: { label: 'Atenção', icon: '!', css: 'warning' },
  Outside: { label: 'Fora da meta', icon: '✕', css: 'exceeded' },
};

/** ADR-0013, regra 7. */
const OBJECTIVE_SITUATION: Record<SavingsObjectiveSituation, { label: string; icon: string; css: string }> = {
  OnTrack: { label: 'No ritmo', icon: '✓', css: 'ontrack' },
  Behind: { label: 'Atrasado', icon: '!', css: 'warning' },
  Reached: { label: 'Atingido', icon: '★', css: 'ontrack' },
  DeadlinePassed: { label: 'Prazo vencido', icon: '✕', css: 'exceeded' },
};

const money = (value: number) => value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

/** Mês corrente no fuso do navegador (`yyyy-MM`, o mesmo do `<input type="month">`). */
function currentMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function today(): string {
  const now = new Date();
  return `${currentMonth()}-${String(now.getDate()).padStart(2, '0')}`;
}

function shiftMonth(month: string, delta: number): string {
  const [year, m] = month.split('-').map(Number);
  const date = new Date(year, m - 1 + delta, 1);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

/** `2026-12` → `12/2026`. */
function monthLabel(month: string): string {
  const [year, m] = month.split('-');
  return `${m}/${year}`;
}

/** `2026-11-21` → `21/11/2026`. */
function dateLabel(date: string): string {
  const [year, m, d] = date.split('-');
  return `${d}/${m}/${year}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/** A forma e o valor da meta em uma frase. */
function goalLabel(kind: MonthlyGoalKind, amount: number | null, percent: number | null): string {
  if (kind === 'SavePercent') return `Guardar ${percent ?? 0}% das entradas`;
  if (kind === 'SpendCeiling') return `Gastar no máximo ${money(amount ?? 0)}`;
  return `Guardar ${money(amount ?? 0)}`;
}

/** Aceita "1.234,56", "1234,56" e "1234.56". */
function parseAmount(text: string): number {
  const clean = text.trim();
  return Number(clean.includes(',') ? clean.replace(/\./g, '').replace(',', '.') : clean);
}

/** MET-7 (ADR-0013): meta do mês de PF e PJ e os objetivos com aportes. */
export function GoalsView() {
  // Muda quando a meta muda, para o histórico buscar de novo.
  const [revision, setRevision] = useState(0);
  return (
    <section>
      <h2>Metas</h2>
      <p className="hint">
        A meta do mês compara o resultado da previsão (entradas − saídas − faturas que vencem no mês) com o que você quer
        guardar ou com um teto de gastos. Os objetivos somam o que você registra como guardado para cada um.
      </p>
      <MonthlyGoals revision={revision} onChanged={() => setRevision((r) => r + 1)} />
      <GoalHistory revision={revision} />
      <Objectives onGoalChanged={() => setRevision((r) => r + 1)} />
    </section>
  );
}

/** MET-3/MET-7: situação do mês, "ainda pode gastar" e a meta de cada escopo. */
function MonthlyGoals({ revision: outside, onChanged }: { revision: number; onChanged: () => void }) {
  const [month, setMonth] = useState(currentMonth());
  const [status, setStatus] = useState<MonthlyGoalStatus | null>(null);
  const [goals, setGoals] = useState<MonthlyGoal[]>([]);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    Promise.all([goalsApi.status(month), goalsApi.listMonthly()])
      .then(([s, g]) => {
        if (cancelled) return;
        setStatus(s);
        setGoals(g);
      })
      .catch((err) => !cancelled && setError(messageOf(err)))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [month, revision, outside]);

  const current = currentMonth();
  const shown = status?.month === month ? status : null;

  function changed(text: string) {
    setNotice(text);
    setRevision((r) => r + 1);
    onChanged();
  }

  return (
    <div className="goal-month" data-cy="goal-month" aria-busy={loading}>
      <div className="month-nav">
        <button
          type="button"
          className="ghost small"
          data-cy="goal-month-prev"
          aria-label="Mês anterior"
          disabled={month <= current}
          onClick={() => setMonth((m) => shiftMonth(m, -1))}
        >
          ‹
        </button>
        <h3 data-cy="goal-month-label" aria-live="polite">
          Meta de {monthLabel(month)}
        </h3>
        <button
          type="button"
          className="ghost small"
          data-cy="goal-month-next"
          aria-label="Próximo mês"
          disabled={month >= shiftMonth(current, 11)}
          onClick={() => setMonth((m) => shiftMonth(m, 1))}
        >
          ›
        </button>
        <button
          type="button"
          className="ghost small"
          data-cy="goal-month-refresh"
          onClick={() => setRevision((r) => r + 1)}
          disabled={loading}
        >
          atualizar
        </button>
      </div>
      {shown && <p className="muted">{shown.notice}</p>}
      {notice && (
        <p className="notice" data-cy="goal-notice">
          {notice}
        </p>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {!shown && loading ? (
        <p>Carregando…</p>
      ) : (
        shown &&
        SCOPES.map(({ scope, label, short }) => (
          <MonthlyScope
            key={scope}
            label={label}
            short={short}
            month={month}
            status={shown[scope]}
            goal={goals.find((g) => g.scope === scope && g.closedFromMonth === null) ?? null}
            onChanged={changed}
          />
        ))
      )}
    </div>
  );
}

function MonthlyScope(props: {
  label: string;
  short: string;
  month: string;
  status: MonthlyGoalScopeStatus;
  goal: MonthlyGoal | null;
  onChanged: (notice: string) => void;
}) {
  const { label, short, month, status, goal, onChanged } = props;
  const [editing, setEditing] = useState<'none' | 'set' | 'close'>('none');
  const [kind, setKind] = useState<MonthlyGoalKind>(status.kind ?? 'SaveAmount');
  const [value, setValue] = useState('');
  const [fromMonth, setFromMonth] = useState(month);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function open(mode: 'set' | 'close') {
    setEditing(mode);
    setError(null);
    setKind(status.kind ?? 'SaveAmount');
    setValue(status.kind === 'SavePercent' ? String(status.percent ?? '') : status.amount != null ? String(status.amount) : '');
    setFromMonth(mode === 'close' ? shiftMonth(month, 1) : month);
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editing === 'close') {
        await goalsApi.closeMonthly(status.scope, fromMonth);
        onChanged(`Meta ${short} encerrada a partir de ${monthLabel(fromMonth)}.`);
      } else {
        const number = parseAmount(value);
        await goalsApi.setMonthly(status.scope, {
          kind,
          amount: kind === 'SavePercent' ? null : number,
          percent: kind === 'SavePercent' ? number : null,
          fromMonth,
        });
        onChanged(`Meta ${short}: ${goalLabel(kind, number, number)} a partir de ${monthLabel(fromMonth)}.`);
      }
      setEditing('none');
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  const situation = status.situation ? MONTH_SITUATION[status.situation] : null;
  const ceiling = status.kind === 'SpendCeiling';

  return (
    <div
      className="budget-group goal-scope"
      data-cy={`goal-scope-${status.scope}`}
      data-situation={status.situation ?? 'none'}
    >
      <div className="budget-status-head">
        <h4>{label}</h4>
        {situation && (
          <span className={`health-badge ${situation.css}`} data-cy="goal-situation">
            <span aria-hidden="true">{situation.icon}</span> {situation.label}
          </span>
        )}
      </div>
      {status.kind ? (
        <p data-cy="goal-kind-label">
          <strong>{goalLabel(status.kind, status.amount, status.percent)}</strong>
          {status.kind === 'SavePercent' && status.target != null && <> (alvo de {money(status.target)} neste mês)</>}
        </p>
      ) : (
        <p className="muted" data-cy="goal-kind-label">
          Sem meta {short} neste mês.
        </p>
      )}
      <dl className="budget-status-figures">
        <div>
          <dt>Entradas</dt>
          <dd data-cy="goal-income">{money(status.income)}</dd>
        </div>
        <div>
          <dt>Saídas + faturas</dt>
          <dd data-cy="goal-outflows">{money(status.outflows)}</dd>
        </div>
        <div>
          <dt>Resultado previsto</dt>
          <dd data-cy="goal-result">{money(status.result)}</dd>
        </div>
        {status.target != null && (
          <div>
            <dt>{ceiling ? 'Teto' : 'Guardar'}</dt>
            <dd data-cy="goal-target">{money(status.target)}</dd>
          </div>
        )}
        {status.canStillSpend != null && (
          <div>
            <dt>Ainda pode gastar</dt>
            <dd data-cy="goal-can-spend">
              <strong>{money(status.canStillSpend)}</strong>
            </dd>
          </div>
        )}
      </dl>

      {editing === 'none' ? (
        <div className="actions">
          <button type="button" className="ghost small" data-cy="goal-edit-open" onClick={() => open('set')}>
            {goal ? 'mudar meta' : 'definir meta'}
          </button>
          {goal && (
            <button type="button" className="ghost small" data-cy="goal-close-open" onClick={() => open('close')}>
              encerrar
            </button>
          )}
        </div>
      ) : (
        <form className="row-form wrap" onSubmit={submit} data-cy="goal-form" aria-label={`Meta ${short}`}>
          {editing === 'set' && (
            <>
              <select data-cy="goal-kind" aria-label="Forma da meta" value={kind} onChange={(e) => setKind(e.target.value as MonthlyGoalKind)}>
                {KINDS.map((k) => (
                  <option key={k.kind} value={k.kind}>
                    {k.label}
                  </option>
                ))}
              </select>
              <input
                data-cy="goal-value"
                aria-label={kind === 'SavePercent' ? 'Percentual das entradas' : 'Valor'}
                placeholder={kind === 'SavePercent' ? '% das entradas' : 'Valor (R$)'}
                inputMode="decimal"
                value={value}
                onChange={(e) => setValue(e.target.value)}
                required
                autoFocus
              />
            </>
          )}
          <label className="inline-label">
            {editing === 'set' ? 'a partir de' : 'deixa de valer a partir de'}
            <input data-cy="goal-from" type="month" value={fromMonth} onChange={(e) => setFromMonth(e.target.value)} required />
          </label>
          <button data-cy="goal-save" className="primary" type="submit" disabled={saving}>
            {saving ? 'Salvando…' : editing === 'close' ? 'Encerrar' : 'Salvar'}
          </button>
          <button type="button" className="ghost" onClick={() => setEditing('none')} disabled={saving}>
            Cancelar
          </button>
        </form>
      )}
      {error && (
        <p className="error" role="alert" data-cy="goal-error">
          {error}
        </p>
      )}
    </div>
  );
}

/** MET-4: os últimos 6 meses fechados, com a meta que valia e se foi atingida. */
function GoalHistory({ revision }: { revision: number }) {
  const [months, setMonths] = useState<MonthlyGoalHistoryMonth[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    goalsApi
      .history(6)
      .then((list) => !cancelled && setMonths(list))
      .catch((err) => !cancelled && setError(messageOf(err)));
    return () => {
      cancelled = true;
    };
  }, [revision]);

  if (error) {
    return (
      <p className="error" role="alert">
        {error}
      </p>
    );
  }

  if (!months.some((m) => m.personal.achieved !== null || m.business.achieved !== null)) {
    return null;
  }

  const cell = (h: MonthlyGoalHistoryScope) =>
    h.achieved === null ? (
      <td className="muted" data-cy={`history-${h.scope}`} data-achieved="none">
        sem meta
      </td>
    ) : (
      <td data-cy={`history-${h.scope}`} data-achieved={h.achieved}>
        <span className={`health-badge ${h.achieved ? 'ontrack' : 'exceeded'}`}>
          <span aria-hidden="true">{h.achieved ? '✓' : '✕'}</span> {h.achieved ? 'Atingida' : 'Não atingida'}
        </span>{' '}
        {h.kind === 'SpendCeiling'
          ? `gastou ${money(h.outflows)} de ${money(h.target ?? 0)}`
          : `guardou ${money(h.result)} de ${money(h.target ?? 0)}`}
      </td>
    );

  return (
    <div className="goal-history" data-cy="goal-history">
      <h4>Últimos meses</h4>
      <table className="data">
        <thead>
          <tr>
            <th>Mês</th>
            <th>Pessoal (PF)</th>
            <th>Empresa (PJ)</th>
          </tr>
        </thead>
        <tbody>
          {[...months].reverse().map((m) => (
            <tr key={m.month} data-cy="goal-history-row" data-month={m.month}>
              <td>{monthLabel(m.month)}</td>
              {cell(m.personal)}
              {cell(m.business)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** MET-5/MET-6/MET-7: objetivos com aportes, ritmo e situação. */
function Objectives({ onGoalChanged }: { onGoalChanged: () => void }) {
  const [objectives, setObjectives] = useState<SavingsObjective[]>([]);
  const [showArchived, setShowArchived] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [scope, setScope] = useState<GoalScope>('personal');
  const [name, setName] = useState('');
  const [target, setTarget] = useState('');
  const [deadline, setDeadline] = useState('');
  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [goalNotice, setGoalNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    goalsApi
      .listObjectives(showArchived)
      .then((list) => !cancelled && setObjectives(list))
      .catch((err) => !cancelled && setError(messageOf(err)))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [showArchived]);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setCreating(true);
    setFormError(null);
    try {
      const created = await goalsApi.createObjective(scope, { name, target: parseAmount(target), deadline: deadline || null });
      setObjectives((list) => [...list, created]);
      setName('');
      setTarget('');
      setDeadline('');
    } catch (err) {
      setFormError(messageOf(err));
    } finally {
      setCreating(false);
    }
  }

  /**
   * MET-10 (ADR-0013, decisão 8): soma do "precisa por mês" dos objetivos em andamento com prazo, por escopo. A
   * meta mensal e os objetivos continuam independentes: o botão só copia a soma para a meta, a partir deste mês.
   */
  const needed = SCOPES.map(({ scope, short }) => ({
    scope,
    short,
    total:
      Math.round(
        objectives
          .filter((o) => o.scope === scope && !o.archived && o.neededPerMonth !== null)
          .reduce((sum, o) => sum + (o.neededPerMonth ?? 0), 0) * 100,
      ) / 100,
  })).filter((n) => n.total > 0);

  async function applyAsGoal(scope: GoalScope, short: string, total: number) {
    setError(null);
    try {
      await goalsApi.setMonthly(scope, { kind: 'SaveAmount', amount: total, fromMonth: currentMonth() });
      setGoalNotice(`Meta ${short}: guardar ${money(total)} a partir de ${monthLabel(currentMonth())}.`);
      onGoalChanged();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  function replace(updated: SavingsObjective) {
    setObjectives((list) =>
      updated.archived && !showArchived ? list.filter((o) => o.id !== updated.id) : list.map((o) => (o.id === updated.id ? updated : o)),
    );
  }

  return (
    <div className="objectives" data-cy="objectives" data-loading={loading}>
      <h3 className="section-title">Objetivos</h3>
      <p className="hint">
        Registre o que você guardou para cada objetivo (uma retirada é um valor negativo). O que você registra aqui não é
        lançamento: não aparece no Resumo, nos orçamentos nem na previsão.
      </p>
      <form className="row-form wrap" onSubmit={create} data-cy="objective-form" aria-label="Novo objetivo">
        <select data-cy="objective-scope" aria-label="Escopo" value={scope} onChange={(e) => setScope(e.target.value as GoalScope)}>
          {SCOPES.map((s) => (
            <option key={s.scope} value={s.scope}>
              {s.label}
            </option>
          ))}
        </select>
        <input
          data-cy="objective-name"
          aria-label="Nome do objetivo"
          placeholder="Ex.: Reserva de emergência"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={60}
          required
        />
        <input
          data-cy="objective-target"
          aria-label="Alvo"
          placeholder="Alvo (R$)"
          inputMode="decimal"
          value={target}
          onChange={(e) => setTarget(e.target.value)}
          required
        />
        <label className="inline-label">
          prazo (opcional)
          <input data-cy="objective-deadline" type="month" value={deadline} onChange={(e) => setDeadline(e.target.value)} />
        </label>
        <button data-cy="objective-submit" className="primary" type="submit" disabled={creating}>
          {creating ? 'Criando…' : 'Criar objetivo'}
        </button>
      </form>
      {formError && (
        <p className="error" role="alert" data-cy="objective-form-error">
          {formError}
        </p>
      )}
      {needed.length > 0 && (
        <div className="objectives-needed" data-cy="objectives-needed">
          {needed.map((n) => (
            <p key={n.scope} data-cy={`objectives-needed-${n.scope}`} data-total={n.total}>
              Para chegar no prazo, os objetivos {n.short} precisam de <strong>{money(n.total)}</strong> por mês.{' '}
              <button type="button" className="ghost small" data-cy={`objectives-use-as-goal-${n.scope}`} onClick={() => void applyAsGoal(n.scope, n.short, n.total)}>
                usar como meta {n.short}
              </button>
            </p>
          ))}
        </div>
      )}
      {goalNotice && (
        <p className="notice" data-cy="objectives-goal-notice">
          {goalNotice}
        </p>
      )}
      <label className="inline-label">
        <input
          data-cy="objectives-show-archived"
          type="checkbox"
          checked={showArchived}
          onChange={(e) => setShowArchived(e.target.checked)}
        />
        mostrar arquivados
      </label>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {loading && objectives.length === 0 ? (
        <p>Carregando…</p>
      ) : objectives.length === 0 ? (
        <p className="muted" data-cy="objectives-empty">
          Nenhum objetivo ainda.
        </p>
      ) : (
        <ul className="budget-status-list">
          {objectives.map((o) => (
            <ObjectiveItem key={o.id} objective={o} onChanged={replace} />
          ))}
        </ul>
      )}
    </div>
  );
}

function ObjectiveItem({ objective: o, onChanged }: { objective: SavingsObjective; onChanged: (o: SavingsObjective) => void }) {
  const [panel, setPanel] = useState<'none' | 'contribute' | 'edit'>('none');
  const [direction, setDirection] = useState<'save' | 'withdraw'>('save');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(today());
  const [note, setNote] = useState('');
  const [name, setName] = useState(o.name);
  const [target, setTarget] = useState(String(o.target));
  const [deadline, setDeadline] = useState(o.deadline ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<SavingsObjective>, after?: () => void) {
    setBusy(true);
    setError(null);
    try {
      onChanged(await action());
      after?.();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  function contribute(e: React.FormEvent) {
    e.preventDefault();
    const value = parseAmount(amount);
    void run(
      () => goalsApi.contribute(o.id, date, direction === 'withdraw' ? -value : value, note.trim() || null),
      () => {
        setAmount('');
        setNote('');
        setPanel('none');
      },
    );
  }

  function edit(e: React.FormEvent) {
    e.preventDefault();
    void run(
      () => goalsApi.editObjective(o.id, { name, target: parseAmount(target), deadline: deadline || null }),
      () => setPanel('none'),
    );
  }

  function openEdit() {
    setName(o.name);
    setTarget(String(o.target));
    setDeadline(o.deadline ?? '');
    setError(null);
    setPanel('edit');
  }

  const situation = OBJECTIVE_SITUATION[o.situation];
  const percent = o.target > 0 ? Math.min(100, (o.saved / o.target) * 100) : 0;
  const scopeShort = o.scope === 'business' ? 'PJ' : 'PF';

  return (
    <li
      className={`budget-status ${situation.css}`}
      data-cy="objective-row"
      data-name={o.name}
      data-situation={o.situation}
      data-archived={o.archived}
    >
      <div className="budget-status-head">
        <strong>
          {o.name} <span className="muted">({scopeShort})</span>
        </strong>
        <span className={`health-badge ${situation.css}`} data-cy="objective-situation">
          <span aria-hidden="true">{situation.icon}</span> {o.archived ? 'Arquivado' : situation.label}
        </span>
      </div>
      <div
        className="progress"
        role="progressbar"
        aria-label={`Guardado de ${o.name}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(percent)}
      >
        <div className="progress-fill" style={{ width: `${percent}%` }} />
      </div>
      <dl className="budget-status-figures">
        <div>
          <dt>Guardado</dt>
          <dd data-cy="objective-saved">
            {money(o.saved)} de {money(o.target)}
          </dd>
        </div>
        <div>
          <dt>Falta</dt>
          <dd data-cy="objective-missing">{money(o.missing)}</dd>
        </div>
        <div>
          <dt>Prazo</dt>
          <dd data-cy="objective-deadline-label">{o.deadline ? monthLabel(o.deadline) : 'sem prazo'}</dd>
        </div>
        {o.neededPerMonth != null && (
          <div>
            <dt>Precisa por mês</dt>
            <dd data-cy="objective-needed">{money(o.neededPerMonth)}</dd>
          </div>
        )}
        <div>
          <dt>Ritmo (últimos 3 meses)</dt>
          <dd data-cy="objective-pace">{money(o.pace)}/mês</dd>
        </div>
        <div>
          <dt>Chegada prevista</dt>
          <dd data-cy="objective-arrival">{o.expectedArrival ? monthLabel(o.expectedArrival) : o.situation === 'Reached' ? 'já chegou' : '—'}</dd>
        </div>
      </dl>

      <div className="actions">
        {o.archived ? (
          <button type="button" className="ghost small" data-cy="objective-unarchive" disabled={busy} onClick={() => void run(() => goalsApi.unarchiveObjective(o.id))}>
            reabrir
          </button>
        ) : (
          <>
            <button type="button" className="ghost small" data-cy="contribution-open" onClick={() => setPanel(panel === 'contribute' ? 'none' : 'contribute')}>
              registrar aporte
            </button>
            <button type="button" className="ghost small" data-cy="objective-edit-open" onClick={openEdit}>
              editar
            </button>
            <button type="button" className="ghost small" data-cy="objective-archive" disabled={busy} onClick={() => void run(() => goalsApi.archiveObjective(o.id))}>
              arquivar
            </button>
          </>
        )}
      </div>

      {panel === 'contribute' && (
        <form className="row-form wrap" onSubmit={contribute} data-cy="contribution-form" aria-label={`Aporte em ${o.name}`}>
          <select data-cy="contribution-direction" aria-label="Guardar ou retirar" value={direction} onChange={(e) => setDirection(e.target.value as 'save' | 'withdraw')}>
            <option value="save">Guardei</option>
            <option value="withdraw">Retirei</option>
          </select>
          <input
            data-cy="contribution-amount"
            aria-label="Valor do aporte"
            placeholder="Valor (R$)"
            inputMode="decimal"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            required
            autoFocus
          />
          <input data-cy="contribution-date" aria-label="Data" type="date" max={today()} value={date} onChange={(e) => setDate(e.target.value)} required />
          <input data-cy="contribution-note" aria-label="Observação" placeholder="Observação (opcional)" maxLength={120} value={note} onChange={(e) => setNote(e.target.value)} />
          <button data-cy="contribution-submit" className="primary" type="submit" disabled={busy}>
            {busy ? 'Salvando…' : 'Registrar'}
          </button>
        </form>
      )}

      {panel === 'edit' && (
        <form className="row-form wrap" onSubmit={edit} data-cy="objective-edit-form" aria-label={`Editar ${o.name}`}>
          <input data-cy="objective-edit-name" aria-label="Nome" value={name} onChange={(e) => setName(e.target.value)} maxLength={60} required />
          <input data-cy="objective-edit-target" aria-label="Alvo" inputMode="decimal" value={target} onChange={(e) => setTarget(e.target.value)} required />
          <label className="inline-label">
            prazo
            <input data-cy="objective-edit-deadline" type="month" value={deadline} onChange={(e) => setDeadline(e.target.value)} />
          </label>
          <button data-cy="objective-edit-save" className="primary" type="submit" disabled={busy}>
            Salvar
          </button>
          <button type="button" className="ghost" onClick={() => setPanel('none')} disabled={busy}>
            Cancelar
          </button>
        </form>
      )}

      {error && (
        <p className="error" role="alert" data-cy="objective-error">
          {error}
        </p>
      )}

      {o.contributions.length > 0 && (
        <details className="objective-contributions">
          <summary data-cy="contributions-toggle">Aportes ({o.contributions.length})</summary>
          <ul>
            {o.contributions.map((c) => (
              <li key={c.id} data-cy="contribution-row" data-amount={c.amount}>
                <span>{dateLabel(c.date)}</span>
                <span className={c.amount < 0 ? 'negative' : ''}>{money(c.amount)}</span>
                {c.note && <span className="muted">{c.note}</span>}
                {!o.archived && (
                  <button
                    type="button"
                    className="link"
                    data-cy="contribution-delete"
                    disabled={busy}
                    onClick={() => void run(() => goalsApi.removeContribution(o.id, c.id))}
                  >
                    excluir
                  </button>
                )}
              </li>
            ))}
          </ul>
        </details>
      )}
    </li>
  );
}
