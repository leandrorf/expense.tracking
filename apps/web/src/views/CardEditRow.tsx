import { useMemo, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { Field, MonthField, Notice, TextAction } from '../ui';
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

  const formSx = { display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'flex-start', mb: 1.5 } as const;

  return (
    <TableRow data-cy="card-edit">
      <TableCell colSpan={colSpan} sx={{ bgcolor: 'background.default' }}>
        <Box component="form" sx={formSx} onSubmit={handleData} data-cy="card-edit-data" aria-label={`Dados de ${card.label}`}>
          <Field dataCy="card-edit-label" label="Apelido" value={label} onChange={setLabel} required />
          <Field dataCy="card-edit-brand" label="Bandeira" value={brand} onChange={setBrand} />
          <Field
            dataCy="card-edit-lastfour"
            label="4 dígitos"
            value={lastFour}
            onChange={setLastFour}
            required
            slotProps={{ htmlInput: { maxLength: 4, inputMode: 'numeric' } }}
            sx={{ width: 110 }}
          />
          <Field
            dataCy="card-edit-limit"
            label="Limite"
            type="number"
            value={limit}
            onChange={setLimit}
            required
            slotProps={{ htmlInput: { step: '0.01', min: '0' } }}
            sx={{ width: 140 }}
          />
          <Button data-cy="card-edit-save" variant="contained" type="submit" disabled={saving} sx={{ height: 40 }}>
            Salvar dados
          </Button>
          <Button variant="outlined" color="inherit" type="button" onClick={onClose} disabled={saving} sx={{ height: 40 }}>
            Fechar
          </Button>
        </Box>

        <Box component="form" sx={formSx} onSubmit={handleCycle} data-cy="card-edit-cycle" aria-label={`Ciclo de ${card.label}`}>
          <Field
            dataCy="card-edit-closing"
            label="fecha dia"
            type="number"
            value={closingDay}
            onChange={setClosingDay}
            required
            slotProps={{ htmlInput: { min: 1, max: 28 } }}
            sx={{ width: 110 }}
          />
          <Field
            dataCy="card-edit-due"
            label="vence dia"
            type="number"
            value={dueDay}
            onChange={setDueDay}
            required
            slotProps={{ htmlInput: { min: 1, max: 28 } }}
            sx={{ width: 110 }}
          />
          <MonthField dataCy="card-edit-from" label="a partir da fatura de" value={fromMonth} onChange={setChosenMonth} required sx={{ width: 200 }} />
          <Button data-cy="card-edit-cycle-save" variant="contained" type="submit" disabled={saving || !possible} sx={{ height: 40 }}>
            Trocar ciclo
          </Button>
        </Box>
        <Typography variant="body2" color={possible ? 'text.secondary' : 'error'} data-cy="card-edit-preview" sx={{ mb: 1.5 }}>
          {!validDays
            ? 'Dias de fechamento e vencimento entre 1 e 28.'
            : preview
              ? `A fatura de ${monthLabel(fromMonth)} vai de ${dateLabel(preview.start)} a ${dateLabel(preview.closing)} e vence ${dateLabel(preview.due)}. As faturas que já fecharam não mudam.`
              : `A fatura de ${monthLabel(fromMonth)} já fechou: escolha uma fatura seguinte.`}
          {skipped.length > 0 && ` A fatura de ${skipped.map(monthLabel).join(' e ')} deixa de existir (o vencimento pula).`}
          {pending.length > 0 && ' A troca pendente é substituída.'}
        </Typography>

        {pending.map((c) => (
          <Notice
            key={c.fromDueMonth}
            dataCy="card-edit-pending"
            action={
              <TextAction
                type="button"
                data-cy="card-edit-undo"
                disabled={saving}
                onClick={() =>
                  void run(() => accountsApi.undoCardCycle(card.id, c.fromDueMonth!), `Troca de ciclo de ${card.label} desfeita.`)
                }
              >
                desfazer
              </TextAction>
            }
          >
            A partir da fatura de {monthLabel(c.fromDueMonth!)}: fecha dia {c.closingDay}, vence dia {c.dueDay}.
          </Notice>
        ))}
        {error && (
          <Notice severity="error" role="alert" dataCy="card-edit-error">
            {error}
          </Notice>
        )}
      </TableCell>
    </TableRow>
  );
}
