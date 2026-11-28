import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/client';
import { recurrencesApi, type Recurrence } from '../api/recurrences';
import { Field, MonthField, Notice, SelectField, TextAction } from '../ui';

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
  // RJT-7: no reajuste pelo índice, o nome dele acompanha o percentual sugerido.
  const indexName = rule.adjustment?.kind === 'Index' && rule.adjustment.index ? INDEX_LABEL[rule.adjustment.index] : null;

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

  function handleAddAmount() {
    void run(
      () => recurrencesApi.setAmount(rule.id, fromPeriod, Number(amount), rule.version),
      `${rule.description}: ${money(Number(amount))} a partir de ${periodLabel(fromPeriod)}.`,
    ).then((ok) => {
      if (!ok) return;
      setFromPeriod('');
      setAmount('');
    });
  }

  function handleApply() {
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

  const formSx = { display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'center', mb: 1.5 } as const;

  return (
    <TableRow data-cy="rec-values">
      <TableCell colSpan={colSpan} sx={{ bgcolor: 'background.default' }}>
        {pending && (
          <Box
            component="form"
            sx={formSx}
            onSubmit={(e: React.FormEvent) => {
              e.preventDefault();
              handleApply();
            }}
            data-cy="rec-adjust-form"
            aria-label={`Reajuste de ${rule.description}`}
          >
            <Typography component="span" variant="body2" data-cy="rec-adjust-summary" sx={{ flexBasis: '100%' }}>
              Reajuste de {periodLabel(pending.period)}: {money(pending.currentAmount)}
              {pending.suggestedAmount != null && pending.rate != null
                ? ` → ${money(pending.suggestedAmount)} (${indexName ? `${indexName} ` : ''}${percentLabel(pending.rate)})`
                : pending.waitingFor
                  ? ` → aguardando o ${indexName ?? 'índice'} de ${periodLabel(pending.waitingFor)}; informe o percentual ou o valor novo`
                  : ' → informe o percentual ou o valor novo'}
            </Typography>
            <SelectField
              dataCy="rec-adjust-by"
              label="Reajustar por"
              value={adjustBy}
              onChange={(v) => {
                const by = v as 'rate' | 'amount';
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
              options={[
                { value: 'rate', label: 'percentual' },
                { value: 'amount', label: 'valor novo' },
              ]}
              sx={{ width: 150 }}
            />
            <Field
              dataCy="rec-adjust-value"
              label={adjustBy === 'rate' ? 'Percentual do reajuste' : 'Valor novo'}
              type="number"
              value={adjustValue}
              onChange={setAdjustValue}
              required
              slotProps={{ htmlInput: { step: adjustBy === 'rate' ? '0.0001' : '0.01' } }}
              sx={{ width: 190 }}
            />
            <Button data-cy="rec-adjust-confirm" variant="contained" type="submit" disabled={saving}>
              Confirmar reajuste
            </Button>
          </Box>
        )}

        <Table data-cy="rec-amounts" sx={{ mb: 1.5, maxWidth: 520 }}>
          <TableBody>
            {rule.amounts.map((a, i) => {
              const started = a.fromPeriod <= now;
              return (
                <TableRow key={a.fromPeriod} data-cy="rec-amount-row" data-period={a.fromPeriod}>
                  <TableCell>{i === 0 ? `desde ${periodLabel(a.fromPeriod)} (início)` : `a partir de ${periodLabel(a.fromPeriod)}`}</TableCell>
                  <TableCell align="right">{money(a.amount)}</TableCell>
                  <TableCell align="right">
                    {i > 0 && !started && (
                      <TextAction
                        danger
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
                      </TextAction>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>

        <Box
          component="form"
          sx={formSx}
          onSubmit={(e: React.FormEvent) => {
            e.preventDefault();
            handleAddAmount();
          }}
          data-cy="rec-amount-form"
          aria-label={`Novo valor de ${rule.description}`}
        >
          <Field
            dataCy="rec-amount-value"
            label="Novo valor"
            type="number"
            value={amount}
            onChange={setAmount}
            required
            slotProps={{ htmlInput: { step: '0.01', min: '0.01' } }}
            sx={{ width: 150 }}
          />
          {yearly ? (
            <Field
              dataCy="rec-amount-from"
              label="a partir de (ano)"
              type="number"
              value={fromPeriod}
              onChange={setFromPeriod}
              required
              slotProps={{ htmlInput: { min: '1900', max: '9999' } }}
              sx={{ width: 160 }}
            />
          ) : (
            <MonthField dataCy="rec-amount-from" label="a partir de" value={fromPeriod} onChange={setFromPeriod} required sx={{ width: 180 }} />
          )}
          <Button data-cy="rec-amount-save" variant="contained" type="submit" disabled={saving}>
            Salvar valor
          </Button>
          <Button variant="outlined" color="inherit" type="button" onClick={onClose} disabled={saving}>
            Fechar
          </Button>
        </Box>
        {error && (
          <Notice severity="error" role="alert" dataCy="rec-values-error">
            {error}
          </Notice>
        )}
      </TableCell>
    </TableRow>
  );
}
