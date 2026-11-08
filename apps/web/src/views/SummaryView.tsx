import { useEffect, useState } from 'react';
import { ApiError } from '../api/client';
import { reportsApi, type ReportSummary, type TotalByKey } from '../api/reports';

const SCOPE_LABEL: Record<string, string> = {
  Personal: 'Pessoal (PF)',
  Business: 'Empresa (PJ)',
  Unknown: 'Desconhecido',
};

export function SummaryView() {
  const [summary, setSummary] = useState<ReportSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setSummary(await reportsApi.summary({ from: from || undefined, to: to || undefined }));
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
        <button onClick={() => void load()}>Aplicar</button>
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
