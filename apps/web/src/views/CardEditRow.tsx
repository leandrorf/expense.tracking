import { useMemo, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import type { CreditCard } from '../api/types';
import {
  addMonths,
  canStartAt,
  dateLabel,
  exists,
  firstPossibleMonth,
  monthLabel,
  pendingChanges,
  periodOf,
  termsOf,
  todayIso,
  withProposed,
} from '../cardCycle';

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface Props {
  card: CreditCard;
  colSpan: number;
  onSaved: (message: string) => Promise<void>;
  onClose: () => void;
}

/**
 * CYC-5 (ADR-0012, decisões 3, 4 e 7): edita os dados do cartão e troca o ciclo a partir de uma fatura, com a
 * prévia da fatura da troca antes de confirmar. As faturas que já fecharam não mudam.
 */
export function CardEditRow({ card, colSpan, onSaved, onClose }: Props) {
  const today = todayIso();
  const [label, setLabel] = useState(card.label);
  const [brand, setBrand] = useState(card.brand);
  const [lastFour, setLastFour] = useState(card.lastFourDigits);
  const [limit, setLimit] = useState(String(card.creditLimit));
  const [closingDay, setClosingDay] = useState(String(card.closingDay));
  const [dueDay, setDueDay] = useState(String(card.dueDay));
  const pending = pendingChanges(card.cycles, card.closingDay, card.dueDay, today);
  // As trocas pendentes serão substituídas: a regra olha só para as que já começaram.
  const started = useMemo(
    () => termsOf((card.cycles ?? []).filter((c) => !pending.includes(c)), card.closingDay, card.dueDay),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [card],
  );
  // Vazio = a sugestao, que acompanha os dias digitados ate o usuario escolher o mes.
  const [chosenMonth, setChosenMonth] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const newClosing = Number(closingDay);
  const newDue = Number(dueDay);
  const validDays = newClosing >= 1 && newClosing <= 28 && newDue >= 1 && newDue <= 28;
  const suggested = validDays ? firstPossibleMonth(started, newClosing, newDue, today) : null;
  const fromMonth = chosenMonth || suggested || addMonths(today.slice(0, 7), 1);
  const proposed = { from: fromMonth, closingDay: newClosing, dueDay: newDue };
  const possible = validDays && fromMonth !== '' && canStartAt(started, proposed, today);
  const next = withProposed(started, proposed);
  const preview = possible ? periodOf(next, fromMonth) : null;
  // A fatura antiga que fecharia depois do primeiro fechamento novo some (o vencimento pula).
  const skipped = possible ? [addMonths(fromMonth, -1), addMonths(fromMonth, -2)].filter((m) => exists(started, m) && !exists(next, m)) : [];

  async function run(action: () => Promise<unknown>, message: string) {
    setSaving(true);
    setError(null);
    try {
      await action();
      await onSaved(message);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  function handleData(e: React.FormEvent) {
    e.preventDefault();
    void run(
      () => accountsApi.editCard(card.id, { label, brand, lastFourDigits: lastFour, creditLimit: Number(limit) }),
      `Cartão ${label} atualizado.`,
    );
  }

  function handleCycle(e: React.FormEvent) {
    e.preventDefault();
    void run(
      () => accountsApi.changeCardCycle(card.id, { closingDay: newClosing, dueDay: newDue, fromDueMonth: fromMonth }),
      `Ciclo de ${card.label} trocado a partir da fatura de ${monthLabel(fromMonth)}.`,
    );
  }

  return (
    <tr className="budget-edit" data-cy="card-edit">
      <td colSpan={colSpan}>
        <form className="row-form wrap" onSubmit={handleData} data-cy="card-edit-data" aria-label={`Dados de ${card.label}`}>
          <input data-cy="card-edit-label" aria-label="Apelido" value={label} onChange={(e) => setLabel(e.target.value)} required />
          <input data-cy="card-edit-brand" aria-label="Bandeira" value={brand} onChange={(e) => setBrand(e.target.value)} />
          <input
            data-cy="card-edit-lastfour"
            aria-label="4 dígitos"
            value={lastFour}
            maxLength={4}
            onChange={(e) => setLastFour(e.target.value)}
            required
          />
          <input
            data-cy="card-edit-limit"
            aria-label="Limite"
            type="number"
            step="0.01"
            min="0"
            value={limit}
            onChange={(e) => setLimit(e.target.value)}
            required
          />
          <button data-cy="card-edit-save" className="primary small" type="submit" disabled={saving}>
            Salvar dados
          </button>
          <button className="ghost small" type="button" onClick={onClose} disabled={saving}>
            Fechar
          </button>
        </form>

        <form className="row-form wrap" onSubmit={handleCycle} data-cy="card-edit-cycle" aria-label={`Ciclo de ${card.label}`}>
          <label className="inline-label">
            fecha dia
            <input
              data-cy="card-edit-closing"
              type="number"
              min={1}
              max={28}
              value={closingDay}
              onChange={(e) => setClosingDay(e.target.value)}
              required
            />
          </label>
          <label className="inline-label">
            vence dia
            <input data-cy="card-edit-due" type="number" min={1} max={28} value={dueDay} onChange={(e) => setDueDay(e.target.value)} required />
          </label>
          <label className="inline-label">
            a partir da fatura de
            <input data-cy="card-edit-from" type="month" value={fromMonth} onChange={(e) => setChosenMonth(e.target.value)} required />
          </label>
          <button data-cy="card-edit-cycle-save" className="primary small" type="submit" disabled={saving || !possible}>
            Trocar ciclo
          </button>
        </form>
        <p className={possible ? 'muted' : 'error'} data-cy="card-edit-preview">
          {!validDays
            ? 'Dias de fechamento e vencimento entre 1 e 28.'
            : preview
              ? `A fatura de ${monthLabel(fromMonth)} vai de ${dateLabel(preview.start)} a ${dateLabel(preview.closing)} e vence ${dateLabel(preview.due)}. As faturas que já fecharam não mudam.`
              : `A fatura de ${monthLabel(fromMonth)} já fechou: escolha uma fatura seguinte.`}
          {skipped.length > 0 && ` A fatura de ${skipped.map(monthLabel).join(' e ')} deixa de existir (o vencimento pula).`}
          {pending.length > 0 && ' A troca pendente é substituída.'}
        </p>

        {pending.map((c) => (
          <p key={c.fromDueMonth} className="notice" data-cy="card-edit-pending">
            A partir da fatura de {monthLabel(c.fromDueMonth!)}: fecha dia {c.closingDay}, vence dia {c.dueDay}.{' '}
            <button
              className="link"
              type="button"
              data-cy="card-edit-undo"
              disabled={saving}
              onClick={() =>
                void run(() => accountsApi.undoCardCycle(card.id, c.fromDueMonth!), `Troca de ciclo de ${card.label} desfeita.`)
              }
            >
              desfazer
            </button>
          </p>
        ))}
        {error && (
          <p className="error" role="alert" data-cy="card-edit-error">
            {error}
          </p>
        )}
      </td>
    </tr>
  );
}
