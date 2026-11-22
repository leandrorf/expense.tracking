import { useEffect, useState } from 'react';
import { ApiError } from '../api/client';
import { forecastApi, type CashForecast, type MonthFlow } from '../api/reports';

type Scope = 'total' | 'personal' | 'business';

const SCOPES: { value: Scope; label: string }[] = [
  { value: 'total', label: 'Total' },
  { value: 'personal', label: 'Pessoal (PF)' },
  { value: 'business', label: 'Empresa (PJ)' },
];

const HORIZONS = [3, 6, 12];

const MONTH_NAMES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

/** `2026-12` → `dez/2026`. */
function monthLabel(month: string): string {
  const [year, m] = month.split('-').map(Number);
  return `${MONTH_NAMES[m - 1]}/${year}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/**
 * INV-9 (ADR-0005, decisão 8): previsão de caixa mês a mês. O cartão entra pela fatura, no mês de
 * vencimento; o resultado desconta o total das faturas, e "falta pagar" mostra o que ainda vence.
 */
export function ForecastView() {
  const [forecast, setForecast] = useState<CashForecast | null>(null);
  const [months, setMonths] = useState(6);
  const [scope, setScope] = useState<Scope>('total');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load(horizon: number) {
    setLoading(true);
    setError(null);
    try {
      setForecast(await forecastApi.get(horizon));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(months);
  }, [months]);

  const currency = forecast?.currency ?? 'BRL';
  const money = (value: number) => value.toLocaleString('pt-BR', { style: 'currency', currency });
  const rows = (forecast?.months ?? []).map((m) => ({ month: m.month, flow: m[scope] as MonthFlow }));
  const sum = (pick: (f: MonthFlow) => number) => rows.reduce((total, r) => total + pick(r.flow), 0);

  return (
    <section data-cy="forecast">
      <h2>Previsão de caixa</h2>

      <div className="filters">
        <label htmlFor="forecast-scope">Escopo:</label>
        <select id="forecast-scope" data-cy="forecast-scope" value={scope} onChange={(e) => setScope(e.target.value as Scope)}>
          {SCOPES.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
        <label htmlFor="forecast-months">Meses:</label>
        <select
          id="forecast-months"
          data-cy="forecast-months"
          value={months}
          onChange={(e) => setMonths(Number(e.target.value))}
        >
          {HORIZONS.map((h) => (
            <option key={h} value={h}>
              {h}
            </option>
          ))}
        </select>
        <button className="ghost small" data-cy="forecast-refresh" onClick={() => void load(months)}>
          atualizar
        </button>
      </div>

      {forecast && (
        <p className="notice" data-cy="forecast-notice">
          {forecast.notice} Compras no cartão entram pela fatura, no mês do vencimento.
        </p>
      )}
      {forecast && forecast.accountsWithoutBalance > 0 && (
        <p className="notice" data-cy="forecast-balance-notice">
          {forecast.accountsWithoutBalance === 1
            ? '1 conta ativa está sem saldo informado e fica fora do saldo projetado.'
            : `${forecast.accountsWithoutBalance} contas ativas estão sem saldo informado e ficam fora do saldo projetado.`}{' '}
          Informe o saldo na aba Contas.
        </p>
      )}

      {error && <p className="error">{error}</p>}
      {loading && !forecast ? (
        <p>Carregando…</p>
      ) : (
        <div className="table-scroll">
          <table className="data" data-cy="forecast-table">
            <thead>
              <tr>
                <th>Mês</th>
                <th className="right">Entradas</th>
                <th className="right">Saídas</th>
                <th className="right">Faturas</th>
                <th className="right">Resultado</th>
                <th className="right" title="Saldo das contas com saldo informado, no fim do mês">
                  Saldo no fim do mês
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ month, flow }) => (
                <tr key={month} data-cy="forecast-row" data-month={month}>
                  <td>{monthLabel(month)}</td>
                  <td className="right" data-cy="forecast-income">{money(flow.income)}</td>
                  <td className="right" data-cy="forecast-expense">{money(flow.expense)}</td>
                  <td className="right" data-cy="forecast-invoices">
                    {money(flow.invoices)}
                    {flow.invoices > 0 && (
                      <span className="sub" data-cy="forecast-invoices-remaining">
                        {flow.invoicesRemaining > 0 ? `falta pagar ${money(flow.invoicesRemaining)}` : 'paga'}
                      </span>
                    )}
                  </td>
                  <td className={`right ${flow.result < 0 ? 'negative' : flow.result > 0 ? 'positive' : ''}`} data-cy="forecast-result">
                    {money(flow.result)}
                  </td>
                  <td
                    className={`right ${flow.balance !== null && flow.balance < 0 ? 'negative' : ''}`}
                    data-cy="forecast-balance"
                  >
                    {flow.balance === null ? <span className="muted">sem saldo</span> : money(flow.balance)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr data-cy="forecast-sum">
                <th>Período</th>
                <th className="right">{money(sum((f) => f.income))}</th>
                <th className="right">{money(sum((f) => f.expense))}</th>
                <th className="right">{money(sum((f) => f.invoices))}</th>
                <th className="right">{money(sum((f) => f.result))}</th>
                <th />
              </tr>
            </tfoot>
          </table>
        </div>
      )}
    </section>
  );
}
