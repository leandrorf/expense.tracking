import { useEffect, useState } from 'react';
import { ApiError } from '../api/client';
import { goalsApi, type MonthlyGoalScopeStatus, type MonthlyGoalSituation, type MonthlyGoalStatus } from '../api/goals';
import { reportsApi, type ReportSummary, type TotalByKey } from '../api/reports';

const SCOPE_LABEL: Record<string, string> = {
  Personal: 'Pessoal (PF)',
  Business: 'Empresa (PJ)',
  Unknown: 'Desconhecido',
};

/** MET-8: o mesmo selo da aba Metas. */
const GOAL_SITUATION: Record<MonthlyGoalSituation, { label: string; css: string }> = {
  Inside: { label: 'Dentro da meta', css: 'ontrack' },
  Attention: { label: 'Atenção', css: 'warning' },
  Outside: { label: 'Fora da meta', css: 'exceeded' },
};

export function SummaryView({ onOpenGoals }: { onOpenGoals?: () => void }) {
  const [summary, setSummary] = useState<ReportSummary | null>(null);
  // MET-8: a meta do mes atual de PF e PJ, num cartao com link para a aba Metas.
  const [goal, setGoal] = useState<MonthlyGoalStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const now = new Date();
      const month = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
      const [loaded, status] = await Promise.all([
        reportsApi.summary({ from: from || undefined, to: to || undefined }),
        goalsApi.status(month).catch(() => null),
      ]);
      setSummary(loaded);
      setGoal(status);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const money = (value: number, currency: string) =>
    value.toLocaleString('pt-BR', { style: 'currency', currency: currency || 'BRL' });

  return (
    <section>
      <h2>Resumo</h2>

      <div className="filters">
        <label>Período:</label>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <span>até</span>
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        <button onClick={() => void load()} data-cy="summary-apply">Aplicar</button>
      </div>

      {error && <p className="error">{error}</p>}
      {loading ? (
        <p>Carregando…</p>
      ) : summary ? (
        <>
          <div className="cards-row" data-cy="summary-stats">
            <Stat label="Gastos" value={money(summary.totalExpense, summary.currency)} tone="expense" cy="stat-expense" />
            <Stat label="Receitas" value={money(summary.totalIncome, summary.currency)} tone="income" cy="stat-income" />
            <Stat label="Saldo" value={money(summary.net, summary.currency)} tone={summary.net >= 0 ? 'income' : 'expense'} cy="stat-net" />
            <Stat label="Lançamentos" value={String(summary.count)} cy="stat-count" />
          </div>

          {goal && (goal.personal.situation || goal.business.situation) && (
            <div className="goal-summary" data-cy="summary-goal">
              <h3>Meta do mês</h3>
              <ul>
                {[goal.personal, goal.business]
                  .filter((s) => s.situation)
                  .map((s) => (
                    <GoalLine key={s.scope} status={s} money={(v) => money(v, 'BRL')} />
                  ))}
              </ul>
              {onOpenGoals && (
                <button type="button" className="link" data-cy="summary-goal-open" onClick={onOpenGoals}>
                  ver metas
                </button>
              )}
            </div>
          )}

          <div className="breakdowns">
            <Breakdown title="Por categoria" items={summary.byCategory} currency={summary.currency} money={money} cy="by-category" />
            <Breakdown
              title="Por escopo (PF/PJ)"
              items={summary.byScope.map((s) => ({ key: SCOPE_LABEL[s.key] ?? s.key, total: s.total }))}
              currency={summary.currency}
              money={money}
              cy="by-scope"
            />
            <Breakdown title="Por meio de pagamento" items={summary.byPaymentMethod} currency={summary.currency} money={money} cy="by-payment-method" />
          </div>
        </>
      ) : null}
    </section>
  );
}

function Stat({ label, value, tone, cy }: { label: string; value: string; tone?: 'expense' | 'income'; cy?: string }) {
  return (
    <div className="stat" data-cy={cy}>
      <span className="stat-label">{label}</span>
      <span className={`stat-value ${tone ?? ''}`}>{value}</span>
    </div>
  );
}

function Breakdown({
  title,
  items,
  currency,
  money,
  cy,
}: {
  title: string;
  items: TotalByKey[];
  currency: string;
  money: (v: number, c: string) => string;
  cy?: string;
}) {
  return (
    <div className="breakdown" data-cy={cy}>
      <h3>{title}</h3>
      {items.length === 0 ? (
        <p className="muted">—</p>
      ) : (
        <ul>
          {items.map((i) => (
            <li key={i.key}>
              <span>{i.key}</span>
              <strong>{money(i.total, currency)}</strong>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function GoalLine({ status, money }: { status: MonthlyGoalScopeStatus; money: (v: number) => string }) {
  const situation = GOAL_SITUATION[status.situation!];
  return (
    <li data-cy={`summary-goal-${status.scope}`} data-situation={status.situation}>
      <span>{status.scope === 'business' ? 'Empresa (PJ)' : 'Pessoal (PF)'}</span>
      <span className={`health-badge ${situation.css}`}>{situation.label}</span>
      <span>ainda pode gastar {money(status.canStillSpend ?? 0)}</span>
    </li>
  );
}
