import { useEffect, useState } from 'react';
import { budgetsApi, type Budget, type BudgetHealth, type BudgetMonth, type BudgetScope } from '../api/budgets';
import { ApiError } from '../api/client';

const SCOPES: { scope: BudgetScope; label: string }[] = [
  { scope: 'Personal', label: 'Pessoal (PF)' },
  { scope: 'Business', label: 'Empresa (PJ)' },
];

/** Mês corrente no fuso do navegador, no formato `yyyy-MM` (o mesmo do `<input type="month">`). */
function currentMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

/** Soma `delta` meses a um `yyyy-MM`. */
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

const money = (value: number, currency: string) =>
  value.toLocaleString('pt-BR', { style: 'currency', currency: currency || 'BRL' });

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/** BDG-5/6: acompanhamento do mês, cadastro, alteração de valor (a partir de um mês) e encerramento. */
export function BudgetsView() {
  const [budgets, setBudgets] = useState<Budget[]>([]);
  // Muda a cada alteração nos orçamentos, para o acompanhamento do mês buscar de novo.
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [category, setCategory] = useState('');
  const [scope, setScope] = useState<BudgetScope>('Personal');
  const [amount, setAmount] = useState('');
  const [startMonth, setStartMonth] = useState(currentMonth());
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setLoadError(null);
    try {
      setBudgets(await budgetsApi.list());
    } catch (err) {
      setLoadError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setFormError(null);
    try {
      const created = await budgetsApi.create({ category, scope, amount: Number(amount), startMonth });
      setBudgets((current) => [...current, created]);
      setRevision((r) => r + 1);
      setCategory('');
      setAmount('');
    } catch (err) {
      setFormError(messageOf(err));
    } finally {
      setSubmitting(false);
    }
  }

  function replace(updated: Budget) {
    setBudgets((current) => current.map((b) => (b.id === updated.id ? updated : b)));
    setRevision((r) => r + 1);
  }

  return (
    <section>
      <h2>Orçamentos</h2>
      <p className="hint">
        Limite mensal de gasto por categoria, separado para PF e PJ. A categoria é comparada sem diferença de maiúsculas
        e acentos.
      </p>

      <MonthTracking revision={revision} />

      <h3 className="section-title">Cadastro</h3>
      <form className="row-form wrap" onSubmit={handleCreate} data-cy="budget-form" aria-label="Novo orçamento">
        <input
          data-cy="budget-category"
          aria-label="Categoria"
          placeholder="Categoria"
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          maxLength={60}
          required
        />
        <select
          data-cy="budget-scope"
          aria-label="Escopo"
          value={scope}
          onChange={(e) => setScope(e.target.value as BudgetScope)}
        >
          {SCOPES.map((s) => (
            <option key={s.scope} value={s.scope}>
              {s.label}
            </option>
          ))}
        </select>
        <input
          data-cy="budget-amount"
          aria-label="Valor mensal"
          placeholder="Valor mensal"
          type="number"
          step="0.01"
          min="0.01"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          required
        />
        <input
          data-cy="budget-start-month"
          aria-label="Vale a partir de"
          title="Vale a partir de"
          type="month"
          value={startMonth}
          onChange={(e) => setStartMonth(e.target.value)}
          required
        />
        <button data-cy="budget-submit" className="primary" type="submit" disabled={submitting}>
          {submitting ? 'Salvando…' : 'Adicionar'}
        </button>
      </form>
      {formError && (
        <p className="error" role="alert" data-cy="budget-form-error">
          {formError}
        </p>
      )}

      {loadError && <p className="error">{loadError}</p>}
      {loading ? (
        <p>Carregando…</p>
      ) : (
        SCOPES.map(({ scope: groupScope, label }) => {
          const group = budgets
            .filter((b) => b.scope === groupScope)
            .sort((a, b) => a.category.localeCompare(b.category, 'pt-BR') || a.startMonth.localeCompare(b.startMonth));
          return (
            <div key={groupScope} className="budget-group" data-cy={`budgets-${groupScope.toLowerCase()}`}>
              <h3>{label}</h3>
              {group.length === 0 ? (
                <p className="muted">Nenhum orçamento {groupScope === 'Personal' ? 'PF' : 'PJ'}.</p>
              ) : (
                <table className="data">
                  <thead>
                    <tr>
                      <th>Categoria</th>
                      <th className="right">Valor atual</th>
                      <th>Vigências</th>
                      <th>Situação</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {group.map((b) => (
                      <BudgetRow key={b.id} budget={b} onChanged={replace} />
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          );
        })
      )}
    </section>
  );
}

type Editing = 'none' | 'amount' | 'close';

function BudgetRow({ budget, onChanged }: { budget: Budget; onChanged: (b: Budget) => void }) {
  const [editing, setEditing] = useState<Editing>('none');
  const [amount, setAmount] = useState(String(budget.amount));
  const [month, setMonth] = useState(currentMonth());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function open(mode: Editing) {
    setEditing(mode);
    setAmount(String(budget.amount));
    setMonth(currentMonth());
    setError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const updated =
        editing === 'amount'
          ? await budgetsApi.changeAmount(budget.id, Number(amount), month)
          : await budgetsApi.close(budget.id, month);
      onChanged(updated);
      setEditing('none');
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  const closed = budget.closedFromMonth !== null;

  return (
    <>
      <tr data-cy="budget-row" data-category={budget.category} className={closed ? 'inactive' : ''}>
        <td>{budget.category}</td>
        <td className="right" data-cy="budget-row-amount">
          {money(budget.amount, budget.currency)}
        </td>
        <td className="muted">
          {budget.limits.map((l) => `${monthLabel(l.fromMonth)}: ${money(l.amount, budget.currency)}`).join(' → ')}
        </td>
        <td data-cy="budget-row-state">
          {closed ? `Encerrado a partir de ${monthLabel(budget.closedFromMonth!)}` : `Desde ${monthLabel(budget.startMonth)}`}
        </td>
        <td className="right">
          <button className="ghost small" data-cy="budget-change" onClick={() => open('amount')} disabled={editing !== 'none'}>
            alterar valor
          </button>
          {!closed && (
            <button className="link-danger" data-cy="budget-close" onClick={() => open('close')} disabled={editing !== 'none'}>
              encerrar
            </button>
          )}
        </td>
      </tr>
      {editing !== 'none' && (
        <tr className="budget-edit">
          <td colSpan={5}>
            <form
              className="row-form"
              onSubmit={handleSubmit}
              data-cy={editing === 'amount' ? 'budget-change-form' : 'budget-close-form'}
              aria-label={editing === 'amount' ? `Alterar valor de ${budget.category}` : `Encerrar ${budget.category}`}
            >
              {editing === 'amount' && (
                <input
                  data-cy="budget-change-amount"
                  aria-label="Novo valor"
                  type="number"
                  step="0.01"
                  min="0.01"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  required
                  autoFocus
                />
              )}
              <label className="inline-label">
                {editing === 'amount' ? 'a partir de' : 'deixa de valer a partir de'}
                <input
                  data-cy="budget-edit-month"
                  type="month"
                  value={month}
                  onChange={(e) => setMonth(e.target.value)}
                  required
                  autoFocus={editing === 'close'}
                />
              </label>
              <button data-cy="budget-edit-submit" className="primary" type="submit" disabled={saving}>
                {saving ? 'Salvando…' : editing === 'amount' ? 'Salvar' : 'Encerrar'}
              </button>
              <button type="button" className="ghost" onClick={() => setEditing('none')} disabled={saving}>
                Cancelar
              </button>
            </form>
            {error && (
              <p className="error" role="alert" data-cy="budget-edit-error">
                {error}
              </p>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

const HEALTH: Record<BudgetHealth, { label: string; icon: string }> = {
  OnTrack: { label: 'Dentro', icon: '✓' },
  Warning: { label: 'Atenção', icon: '!' },
  Exceeded: { label: 'Estourado', icon: '✕' },
};

const percentLabel = (value: number) => `${value.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%`;

/** BDG-6: limite, consumido, restante e situação de cada orçamento num mês, com navegação entre meses. */
function MonthTracking({ revision }: { revision: number }) {
  const [month, setMonth] = useState(currentMonth());
  const [data, setData] = useState<BudgetMonth | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    budgetsApi
      .status(month)
      .then((result) => !cancelled && setData(result))
      .catch((err) => !cancelled && setError(messageOf(err)))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [month, revision]);

  // Descarta resposta de outro mês enquanto a nova não chega.
  const statuses = data?.month === month ? data.budgets : [];

  return (
    <div className="budget-month" data-cy="budget-month" aria-busy={loading}>
      <div className="month-nav">
        <button
          type="button"
          className="ghost small"
          data-cy="budget-month-prev"
          aria-label="Mês anterior"
          onClick={() => setMonth((m) => shiftMonth(m, -1))}
        >
          ‹
        </button>
        <h3 data-cy="budget-month-label" aria-live="polite">
          Acompanhamento de {monthLabel(month)}
        </h3>
        <button
          type="button"
          className="ghost small"
          data-cy="budget-month-next"
          aria-label="Próximo mês"
          onClick={() => setMonth((m) => shiftMonth(m, 1))}
        >
          ›
        </button>
        {month !== currentMonth() && (
          <button type="button" className="ghost small" data-cy="budget-month-current" onClick={() => setMonth(currentMonth())}>
            mês atual
          </button>
        )}
      </div>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {loading && statuses.length === 0 ? (
        <p>Carregando…</p>
      ) : (
        SCOPES.map(({ scope, label }) => {
          const group = statuses.filter((s) => s.scope === scope);
          return (
            <div key={scope} className="budget-group" data-cy={`budget-status-${scope.toLowerCase()}`}>
              <h4>{label}</h4>
              {group.length === 0 ? (
                <p className="muted">Nenhum orçamento {scope === 'Personal' ? 'PF' : 'PJ'} vale neste mês.</p>
              ) : (
                <ul className="budget-status-list">
                  {group.map((s) => {
                    const health = HEALTH[s.status];
                    return (
                      <li
                        key={s.budgetId}
                        className={`budget-status ${s.status.toLowerCase()}`}
                        data-cy="budget-status-row"
                        data-category={s.category}
                        data-status={s.status}
                      >
                        <div className="budget-status-head">
                          <strong>{s.category}</strong>
                          <span className={`health-badge ${s.status.toLowerCase()}`} data-cy="budget-status-health">
                            <span aria-hidden="true">{health.icon}</span> {health.label}
                          </span>
                        </div>
                        <div
                          className="progress"
                          role="progressbar"
                          aria-label={`Consumido de ${s.category}`}
                          aria-valuemin={0}
                          aria-valuemax={100}
                          aria-valuenow={Math.min(100, Math.round(s.percent))}
                          aria-valuetext={percentLabel(s.percent)}
                        >
                          <div className="progress-fill" style={{ width: `${Math.min(100, s.percent)}%` }} />
                        </div>
                        <dl className="budget-status-figures">
                          <div>
                            <dt>Limite</dt>
                            <dd data-cy="budget-status-limit">{money(s.limit, s.currency)}</dd>
                          </div>
                          <div>
                            <dt>Consumido</dt>
                            <dd data-cy="budget-status-spent">{money(s.spent, s.currency)}</dd>
                          </div>
                          <div>
                            <dt>{s.remaining < 0 ? 'Passou' : 'Restante'}</dt>
                            <dd data-cy="budget-status-remaining">{money(Math.abs(s.remaining), s.currency)}</dd>
                          </div>
                          <div>
                            <dt>Uso</dt>
                            <dd data-cy="budget-status-percent">{percentLabel(s.percent)}</dd>
                          </div>
                        </dl>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          );
        })
      )}
    </div>
  );
}
