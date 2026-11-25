import { useState } from 'react';
import { ApiError } from '../api/client';
import { recurrencesApi, type Recurrence } from '../api/recurrences';

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];
const INDEX_LABEL: Record<string, string> = {
  IPCA: 'IPCA',
  INPC: 'INPC',
  IGPM: 'IGP-M',
};

/** `2026-12` → `dez/2026`; `2027` → `2027`. */
export function periodLabel(period: string): string {
  const [y, m] = period.split('-');
  return m ? `${MONTHS[Number(m) - 1]}/${y}` : y;
}

/** Período de hoje na frequência da regra: `yyyy-MM` ou `yyyy`. */
function currentPeriod(frequency: Recurrence['frequency']): string {
  const d = new Date();
  const year = String(d.getFullYear());
  return frequency === 'Yearly' ? year : `${year}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

/** "5%" ou "4,5%". */
export function percentLabel(rate: number): string {
  return `${rate.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%`;
}

/** Como o reajuste está configurado, em uma linha ("todo mar pelo IPCA", "todo ano em mar, 5%"). */
export function adjustmentLabel(r: Recurrence): string | null {
  const a = r.adjustment;
  if (!a) return null;
  const how = a.kind === 'Index' ? `pelo ${INDEX_LABEL[a.index ?? ''] ?? a.index}` : percentLabel(a.rate ?? 0);
  return `reajuste em ${MONTHS[a.anniversaryMonth - 1]}: ${how}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/**
 * RJT-5 (ADR-0010, decisões 1, 2, 4 e 5): os valores da regra por vigência ("a partir de"), um valor novo a
 * partir de um período e, no mês do aniversário, a confirmação do reajuste pelo percentual ou pelo valor novo.
 */
export function RecurrenceValuesRow({
  rule,
  colSpan,
  onChanged,
  onClose,
}: {
  rule: Recurrence;
  colSpan: number;
  onChanged: (message: string) => Promise<void>;
  onClose: () => void;
}) {
  const yearly = rule.frequency === 'Yearly';
  const pending = rule.pendingAdjustment;
  const [fromPeriod, setFromPeriod] = useState('');
  const [amount, setAmount] = useState('');
  const [adjustBy, setAdjustBy] = useState<'rate' | 'amount'>('rate');
  const [adjustValue, setAdjustValue] = useState(pending?.rate != null ? String(pending.rate) : '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const money = (value: number) =>
    value.toLocaleString('pt-BR', {
      style: 'currency',
      currency: rule.currency,
    });
  const now = currentPeriod(rule.frequency);

  async function run(action: () => Promise<unknown>, message: string): Promise<boolean> {
    setSaving(true);
    setError(null);
    try {
      await action();
      await onChanged(message);
      return true;
    } catch (err) {
      setError(
        err instanceof ApiError && err.code === 'recurrence.concurrency_conflict'
          ? 'Esta recorrência mudou em outro lugar. A lista foi recarregada; tente de novo.'
          : messageOf(err),
      );
      if (err instanceof ApiError && err.code === 'recurrence.concurrency_conflict') await onChanged('');
      return false;
    } finally {
      setSaving(false);
    }
  }

  function handleAddAmount(e: React.FormEvent) {
    e.preventDefault();
    void run(
      () => recurrencesApi.setAmount(rule.id, fromPeriod, Number(amount), rule.version),
      `${rule.description}: ${money(Number(amount))} a partir de ${periodLabel(fromPeriod)}.`,
    ).then((ok) => {
      if (!ok) return;
      setFromPeriod('');
      setAmount('');
    });
  }

  function handleApply(e: React.FormEvent) {
    e.preventDefault();
    if (!pending) return;
    const value = Number(adjustValue);
    void run(
      () =>
        recurrencesApi.applyAdjustment(rule.id, {
          year: pending.year,
          version: rule.version,
          ...(adjustBy === 'rate' ? { rate: value } : { amount: value }),
        }),
      `Reajuste de ${rule.description} confirmado a partir de ${periodLabel(pending.period)}.`,
    );
  }

  return (
    <tr className="budget-edit" data-cy="rec-values">
      <td colSpan={colSpan}>
        {pending && (
          <form
            className="row-form"
            onSubmit={handleApply}
            data-cy="rec-adjust-form"
            aria-label={`Reajuste de ${rule.description}`}
          >
            <span data-cy="rec-adjust-summary">
              Reajuste de {periodLabel(pending.period)}: {money(pending.currentAmount)}
              {pending.suggestedAmount != null && pending.rate != null
                ? ` → ${money(pending.suggestedAmount)} (${percentLabel(pending.rate)})`
                : ' → informe o percentual ou o valor novo'}
            </span>
            <select
              data-cy="rec-adjust-by"
              aria-label="Reajustar por"
              value={adjustBy}
              onChange={(e) => {
                const by = e.target.value as 'rate' | 'amount';
                setAdjustBy(by);
                setAdjustValue(
                  by === 'rate'
                    ? pending.rate != null
                      ? String(pending.rate)
                      : ''
                    : pending.suggestedAmount != null
                      ? String(pending.suggestedAmount)
                      : '',
                );
              }}
            >
              <option value="rate">percentual</option>
              <option value="amount">valor novo</option>
            </select>
            <input
              data-cy="rec-adjust-value"
              aria-label={adjustBy === 'rate' ? 'Percentual do reajuste' : 'Valor novo'}
              type="number"
              step={adjustBy === 'rate' ? '0.0001' : '0.01'}
              value={adjustValue}
              onChange={(e) => setAdjustValue(e.target.value)}
              required
            />
            <button data-cy="rec-adjust-confirm" className="primary small" type="submit" disabled={saving}>
              Confirmar reajuste
            </button>
          </form>
        )}

        <table className="data compact" data-cy="rec-amounts">
          <tbody>
            {rule.amounts.map((a, i) => {
              const started = a.fromPeriod <= now;
              return (
                <tr key={a.fromPeriod} data-cy="rec-amount-row" data-period={a.fromPeriod}>
                  <td>{i === 0 ? `desde ${periodLabel(a.fromPeriod)} (início)` : `a partir de ${periodLabel(a.fromPeriod)}`}</td>
                  <td className="right">{money(a.amount)}</td>
                  <td className="row-actions">
                    {i > 0 && !started && (
                      <button
                        className="link-danger"
                        data-cy="rec-amount-remove"
                        disabled={saving}
                        onClick={() =>
                          void run(
                            () => recurrencesApi.removeAmount(rule.id, a.fromPeriod, rule.version),
                            `${rule.description}: valor de ${periodLabel(a.fromPeriod)} removido.`,
                          )
                        }
                      >
                        remover
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        <form
          className="row-form"
          onSubmit={handleAddAmount}
          data-cy="rec-amount-form"
          aria-label={`Novo valor de ${rule.description}`}
        >
          <input
            data-cy="rec-amount-value"
            aria-label="Novo valor"
            placeholder="Novo valor"
            type="number"
            step="0.01"
            min="0.01"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            required
          />
          <label className="inline-label">
            a partir de
            {yearly ? (
              <input
                data-cy="rec-amount-from"
                type="number"
                min="1900"
                max="9999"
                placeholder="ano"
                value={fromPeriod}
                onChange={(e) => setFromPeriod(e.target.value)}
                required
              />
            ) : (
              <input
                data-cy="rec-amount-from"
                type="month"
                value={fromPeriod}
                onChange={(e) => setFromPeriod(e.target.value)}
                required
              />
            )}
          </label>
          <button data-cy="rec-amount-save" className="primary small" type="submit" disabled={saving}>
            Salvar valor
          </button>
          <button className="ghost small" type="button" onClick={onClose} disabled={saving}>
            Fechar
          </button>
        </form>
        {error && (
          <p className="error" role="alert" data-cy="rec-values-error">
            {error}
          </p>
        )}
      </td>
    </tr>
  );
}
