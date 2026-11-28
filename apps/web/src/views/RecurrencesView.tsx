import { Fragment, useEffect, useMemo, useState, type ReactNode } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import InputAdornment from '@mui/material/InputAdornment';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import {
  recurrencesApi,
  type DueOccurrence,
  type EconomicIndex,
  type Frequency,
  type Recurrence,
  type RecurrenceInput,
} from '../api/recurrences';
import type { Account, CreditCard, PaymentMethod, Transaction } from '../api/types';
import { adjustmentLabel, periodLabel, RecurrenceValuesRow } from './RecurrenceValuesRow';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';
import { DateField, Field, Notice, RowForm, SelectField, TableBox, TextAction } from '../ui';

const MONTH_NAMES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro'];

function today(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** `2026-12-10` → `10/12/2026`. */
function dateLabel(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

/** "todo dia 10" ou "todo ano em 15/03" (mês sem o dia usa o último). */
function scheduleLabel(r: Pick<Recurrence, 'frequency' | 'startsOn'>): string {
  const [, m, d] = r.startsOn.split('-');
  return r.frequency === 'Yearly' ? `todo ano em ${d}/${m}` : `todo dia ${Number(d)}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface LaunchDraft {
  key: string;
  recurrenceId: string;
  period: string;
  amount: string;
  occurredOn: string;
}

/**
 * REC-5 (ADR-0008): regras de recorrência e o que há "a lançar". A regra só entra na previsão; vira
 * lançamento quando o usuário lança a ocorrência (com o valor real) ou some quando a pula.
 */
/**
 * `template` (REC-10): lançamento escolhido em "repetir todo mês". O formulário abre com os dados dele, a
 * agenda ancorada na data dele, e ele vira a ocorrência do próprio período (a próxima é a do mês seguinte).
 */
export function RecurrencesView({
  template = null,
  onTemplateUsed,
  focusValuesOf = null,
  onFocusUsed,
}: {
  template?: Transaction | null;
  onTemplateUsed?: () => void;
  /** RJT-9: regra cujos valores abrem já (aviso de reajuste pendente). */
  focusValuesOf?: string | null;
  onFocusUsed?: () => void;
} = {}) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([]);
  const [rules, setRules] = useState<Recurrence[]>([]);
  const [due, setDue] = useState<DueOccurrence[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [editing, setEditing] = useState<Recurrence | null>(null);
  const [conflict, setConflict] = useState(false);
  const [accountId, setAccountId] = useState('');
  const [type, setType] = useState<'Expense' | 'Income'>('Expense');
  const [paymentMethod, setPaymentMethod] = useState('');
  const [creditCardId, setCreditCardId] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('');
  const [amount, setAmount] = useState('');
  const [frequency, setFrequency] = useState<Frequency>('Monthly');
  const [startsOn, setStartsOn] = useState(today());
  const [endsOn, setEndsOn] = useState('');
  const [notes, setNotes] = useState('');
  // RJT-5: reajuste anual (vazio = sem reajuste).
  const [adjKind, setAdjKind] = useState<'' | 'Fixed' | 'Index'>('');
  const [adjMonth, setAdjMonth] = useState('');
  const [adjRate, setAdjRate] = useState('');
  const [adjIndex, setAdjIndex] = useState<EconomicIndex>('IPCA');
  const [adjNoReduction, setAdjNoReduction] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [valuesOpen, setValuesOpen] = useState<string | null>(null);

  const [launch, setLaunch] = useState<LaunchDraft | null>(null);
  const [fromTransaction, setFromTransaction] = useState<Transaction | null>(null);
  const { categories, remember } = useCategorySuggestions();

  const requiresCard = useMemo(
    () => paymentMethods.find((m) => m.code === paymentMethod)?.requiresCard ?? false,
    [paymentMethods, paymentMethod],
  );

  async function loadRules() {
    const [list, pending] = await Promise.all([recurrencesApi.list(), recurrencesApi.due()]);
    setRules(list);
    setDue(pending);
  }

  async function loadAll() {
    setLoading(true);
    setError(null);
    try {
      const [accs, crds, methods] = await Promise.all([
        accountsApi.list(),
        accountsApi.listCards(),
        accountsApi.paymentMethods(),
      ]);
      setAccounts(accs);
      setCards(crds);
      setPaymentMethods(methods);
      setAccountId((current) => current || accs.find((a) => a.isActive)?.id || accs[0]?.id || '');
      setPaymentMethod((current) => current || methods[0]?.code || '');
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // REC-10: preenche o formulário com o lançamento de origem (uma vez, depois de carregar as contas).
  useEffect(() => {
    if (!template || loading) return;
    resetForm();
    setFromTransaction(template);
    setAccountId(template.accountId);
    setType(template.type);
    setPaymentMethod(template.paymentMethod);
    setCreditCardId(template.creditCardId ?? '');
    setDescription(template.description);
    setCategory(template.category);
    setAmount(String(template.amount));
    setFrequency('Monthly');
    setStartsOn(template.occurredOn);
    setNotes(template.notes ?? '');
    onTemplateUsed?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [template, loading]);

  // RJT-9: o aviso de reajuste abre os valores da regra (depois de carregar a lista).
  useEffect(() => {
    if (!focusValuesOf || loading) return;
    setValuesOpen(focusValuesOf);
    onFocusUsed?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusValuesOf, loading]);

  function resetForm() {
    setEditing(null);
    setFromTransaction(null);
    setConflict(false);
    setType('Expense');
    setCreditCardId('');
    setDescription('');
    setCategory('');
    setAmount('');
    setFrequency('Monthly');
    setStartsOn(today());
    setEndsOn('');
    setNotes('');
    setAdjKind('');
    setAdjMonth('');
    setAdjRate('');
    setAdjIndex('IPCA');
    setAdjNoReduction(true);
  }

  function startEdit(r: Recurrence) {
    setEditing(r);
    setAccountId(r.accountId);
    setType(r.type);
    setPaymentMethod(r.paymentMethod);
    setCreditCardId(r.creditCardId ?? '');
    setDescription(r.description);
    setCategory(r.category);
    setAmount(String(r.amount));
    setFrequency(r.frequency);
    setStartsOn(r.startsOn);
    setEndsOn(r.endsOn ?? '');
    setNotes(r.notes ?? '');
    setAdjKind(r.adjustment?.kind ?? '');
    setAdjMonth(r.adjustment ? String(r.adjustment.anniversaryMonth) : '');
    setAdjRate(r.adjustment?.rate != null ? String(r.adjustment.rate) : '');
    setAdjIndex(r.adjustment?.index ?? 'IPCA');
    setAdjNoReduction(r.adjustment?.noReduction ?? true);
    setError(null);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  async function handleSubmit() {
    setSubmitting(true);
    setError(null);
    setNotice(null);
    setConflict(false);
    const input: RecurrenceInput = {
      accountId,
      type,
      paymentMethod,
      creditCardId: requiresCard && creditCardId ? creditCardId : undefined,
      description,
      category,
      amount: Number(amount),
      frequency,
      startsOn,
      endsOn: endsOn || undefined,
      notes: notes.trim() || undefined,
      fromTransactionId: !editing && fromTransaction ? fromTransaction.id : undefined,
      // RJT-5: o PUT leva o reajuste inteiro; sem ele, a regra fica sem reajuste.
      adjustment: adjKind
        ? {
            kind: adjKind,
            anniversaryMonth: frequency === 'Yearly' || !adjMonth ? undefined : Number(adjMonth),
            rate: adjKind === 'Fixed' ? Number(adjRate) : undefined,
            index: adjKind === 'Index' ? adjIndex : undefined,
            noReduction: adjNoReduction,
          }
        : undefined,
    };
    try {
      if (editing) {
        await recurrencesApi.update(editing.id, { ...input, version: editing.version });
      } else {
        await recurrencesApi.create(input);
      }
      remember(category);
      resetForm();
      await loadRules();
    } catch (err) {
      if (editing && err instanceof ApiError && err.code === 'recurrence.concurrency_conflict') {
        const current = (await recurrencesApi.list()).find((r) => r.id === editing.id);
        if (current) startEdit(current);
        setConflict(true);
      } else {
        setError(messageOf(err));
      }
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemove(r: Recurrence) {
    if (!window.confirm(`Excluir a recorrência "${r.description}"? Os lançamentos já feitos a partir dela continuam.`)) return;
    try {
      await recurrencesApi.remove(r.id);
      if (editing?.id === r.id) resetForm();
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  function openLaunch(recurrenceId: string, period: string, estimated: number, scheduledOn: string) {
    setLaunch({ key: `${recurrenceId}:${period}`, recurrenceId, period, amount: String(estimated), occurredOn: scheduledOn });
    setError(null);
  }

  async function handleLaunch() {
    if (!launch) return;
    setError(null);
    try {
      const tx = await recurrencesApi.launch(launch.recurrenceId, launch.period, {
        amount: Number(launch.amount),
        occurredOn: launch.occurredOn,
      });
      setNotice(`Lançado: ${tx.description} em ${dateLabel(tx.occurredOn)}.`);
      setLaunch(null);
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  async function handleSkip(recurrenceId: string, period: string, label: string) {
    setError(null);
    try {
      await recurrencesApi.skip(recurrenceId, period);
      setNotice(`Pulado: ${label} de ${periodLabel(period)}.`);
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  const startMonth = Number(startsOn.split('-')[1]) || 1;

  async function handleValuesChanged(message: string) {
    if (message) setNotice(message);
    await loadRules();
  }

  const accountName = (id: string) => accounts.find((a) => a.id === id)?.name ?? '—';
  const accountCards = cards.filter((c) => c.accountId === accountId && (c.isActive || c.id === editing?.creditCardId));
  const money = (value: number, currency = 'BRL') => value.toLocaleString('pt-BR', { style: 'currency', currency });
  const signed = (t: 'Expense' | 'Income', value: number, currency?: string) =>
    `${t === 'Income' ? '+' : '−'}${money(value, currency)}`;

  const launchForm = (key: string) =>
    launch?.key === key && (
      <Box
        component="form"
        onSubmit={(e: React.FormEvent) => {
          e.preventDefault();
          void handleLaunch();
        }}
        data-cy="rec-launch-form"
        sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'center', justifyContent: 'flex-end' }}
      >
        <Field
          dataCy="rec-launch-amount"
          label="Valor real"
          type="number"
          value={launch.amount}
          onChange={(v) => setLaunch({ ...launch, amount: v })}
          required
          slotProps={{ htmlInput: { step: '0.01' } }}
          sx={{ width: 130 }}
        />
        <DateField
          dataCy="rec-launch-date"
          label="Data"
          value={launch.occurredOn}
          onChange={(v) => setLaunch({ ...launch, occurredOn: v })}
          required
          sx={{ width: 160 }}
        />
        <Button data-cy="rec-launch-save" variant="contained" type="submit">
          Lançar
        </Button>
        <Button variant="outlined" color="inherit" type="button" onClick={() => setLaunch(null)}>
          Cancelar
        </Button>
      </Box>
    );

  const title = (
    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
      Recorrências
    </Typography>
  );

  if (loading) {
    return (
      <section>
        {title}
        <Typography>Carregando…</Typography>
      </section>
    );
  }

  return (
    <section data-cy="recurrences">
      {title}
      <Notice>
        Salário, aluguel e assinaturas cadastrados aqui entram na Previsão até você lançar ou pular cada ocorrência. Eles
        não aparecem no Resumo, nos orçamentos nem na exportação antes de serem lançados.
      </Notice>

      {error && <Notice severity="error">{error}</Notice>}
      {notice && (
        <Notice severity="success" dataCy="rec-notice">
          {notice}
        </Notice>
      )}

      <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
        A lançar
      </Typography>
      {due.length === 0 ? (
        <Typography color="text.secondary" data-cy="rec-due-empty" sx={{ mb: 2 }}>
          Nada pendente dos últimos 60 dias.
        </Typography>
      ) : (
        <TableBox>
          <Table data-cy="rec-due" sx={{ minWidth: 720 }}>
            <TableHead>
              <TableRow>
                <TableCell>Data</TableCell>
                <TableCell>Descrição</TableCell>
                <TableCell>Conta</TableCell>
                <TableCell align="right">Valor estimado</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {due.map((d) => {
                const key = `${d.recurrenceId}:${d.period}`;
                return (
                  <TableRow key={key} data-cy="rec-due-row" data-period={d.period} sx={{ verticalAlign: 'top' }}>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{dateLabel(d.scheduledOn)}</TableCell>
                    <TableCell>
                      {d.description}
                      <Sub>{d.category}</Sub>
                    </TableCell>
                    <TableCell>{accountName(d.accountId)}</TableCell>
                    <TableCell align="right" sx={{ whiteSpace: 'nowrap', color: d.type === 'Income' ? 'success.main' : 'error.main' }}>
                      {signed(d.type, d.amount, d.currency)}
                    </TableCell>
                    <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                      {launchForm(key) || (
                        <>
                          <TextAction data-cy="rec-due-launch" onClick={() => openLaunch(d.recurrenceId, d.period, d.amount, d.scheduledOn)}>
                            lançar
                          </TextAction>
                          <TextAction data-cy="rec-due-skip" onClick={() => void handleSkip(d.recurrenceId, d.period, d.description)}>
                            pular
                          </TextAction>
                        </>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableBox>
      )}

      <Typography variant="h3" component="h3" sx={{ mb: 1, mt: 1 }}>
        {editing ? `Editando: ${editing.description}` : 'Nova recorrência'}
      </Typography>
      {accounts.length === 0 ? (
        <Typography color="text.secondary" sx={{ mb: 2 }}>
          Crie uma conta antes de cadastrar recorrências.
        </Typography>
      ) : (
        <>
          {fromTransaction && !editing && (
            <Notice
              dataCy="rec-from-transaction"
              action={
                <TextAction type="button" onClick={resetForm}>
                  desistir
                </TextAction>
              }
            >
              Repetindo <strong>{fromTransaction.description}</strong>: o lançamento de {dateLabel(fromTransaction.occurredOn)}{' '}
              conta como a ocorrência daquele mês, e a próxima fica para o mês seguinte.
            </Notice>
          )}
          {conflict && (
            <Notice severity="warning" dataCy="rec-conflict">
              Esta recorrência mudou enquanto você editava. O formulário mostra agora o que está gravado; refaça a
              alteração e salve de novo.
            </Notice>
          )}
          <RowForm onSubmit={() => void handleSubmit()} dataCy="rec-form">
            <SelectField
              dataCy="rec-account"
              label="Conta"
              value={accountId}
              disabled={editing !== null}
              title={editing ? 'A conta de uma recorrência não muda: exclua e crie de novo na outra conta.' : undefined}
              onChange={setAccountId}
              options={accounts.filter((a) => a.isActive || a.id === accountId).map((a) => ({ value: a.id, label: a.name }))}
            />
            <SelectField
              dataCy="rec-type"
              label="Tipo"
              value={type}
              onChange={(v) => setType(v as 'Expense' | 'Income')}
              options={[
                { value: 'Expense', label: 'Gasto' },
                { value: 'Income', label: 'Receita' },
              ]}
            />
            <SelectField
              dataCy="rec-payment-method"
              label="Meio"
              value={paymentMethod}
              onChange={setPaymentMethod}
              options={paymentMethods.map((m) => ({ value: m.code, label: m.name }))}
            />
            {requiresCard && (
              <SelectField dataCy="rec-card" label="Cartão" value={creditCardId} onChange={setCreditCardId} required>
                <option value="">Selecione o cartão…</option>
                {accountCards.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.label} •••• {c.lastFourDigits}
                  </option>
                ))}
              </SelectField>
            )}
            <Field dataCy="rec-description" label="Descrição" value={description} onChange={setDescription} required />
            <Field
              dataCy="rec-category"
              label="Categoria"
              value={category}
              onChange={setCategory}
              required
              slotProps={{ htmlInput: { list: 'rec-category-suggestions', autoComplete: 'off' } }}
            />
            <CategoryDatalist id="rec-category-suggestions" categories={categories} />
            <Field
              dataCy="rec-amount"
              label="Valor estimado"
              type="number"
              value={amount}
              onChange={setAmount}
              required
              slotProps={{ htmlInput: { step: '0.01' } }}
            />
            <SelectField
              dataCy="rec-frequency"
              label="Frequência"
              value={frequency}
              onChange={(v) => setFrequency(v as Frequency)}
              options={[
                { value: 'Monthly', label: 'Todo mês' },
                { value: 'Yearly', label: 'Todo ano' },
              ]}
            />
            <DateField
              dataCy="rec-starts-on"
              label="a partir de"
              title="O dia (e, na anual, o mês) desta data define quando a recorrência se repete"
              value={startsOn}
              onChange={setStartsOn}
              required
            />
            <DateField
              dataCy="rec-ends-on"
              label="até (opcional)"
              value={endsOn}
              onChange={setEndsOn}
              slotProps={{ htmlInput: { min: startsOn } }}
            />
            <Field
              dataCy="rec-notes"
              label="Observações (opcional)"
              value={notes}
              onChange={setNotes}
              slotProps={{ htmlInput: { maxLength: 500 } }}
            />
            <SelectField
              dataCy="rec-adjust-kind"
              label="Reajuste anual"
              value={adjKind}
              onChange={(v) => setAdjKind(v as '' | 'Fixed' | 'Index')}
              options={[
                { value: '', label: 'Sem reajuste anual' },
                { value: 'Fixed', label: 'Reajuste anual por percentual' },
                { value: 'Index', label: 'Reajuste anual pelo índice' },
              ]}
            />
            {adjKind && (
              <>
                <SelectField
                  dataCy="rec-adjust-month"
                  label="Mês do reajuste"
                  value={frequency === 'Yearly' ? String(startMonth) : adjMonth || String(startMonth)}
                  disabled={frequency === 'Yearly'}
                  title={frequency === 'Yearly' ? 'Na recorrência anual, o reajuste é no mês dela.' : undefined}
                  onChange={setAdjMonth}
                  options={MONTH_NAMES.map((name, i) => ({ value: String(i + 1), label: name }))}
                />
                {adjKind === 'Fixed' ? (
                  <Field
                    dataCy="rec-adjust-rate"
                    label="Percentual do reajuste"
                    type="number"
                    value={adjRate}
                    onChange={setAdjRate}
                    required
                    slotProps={{
                      htmlInput: { step: '0.0001' },
                      input: { endAdornment: <InputAdornment position="end">%</InputAdornment> },
                    }}
                  />
                ) : (
                  <SelectField
                    dataCy="rec-adjust-index"
                    label="Índice do reajuste"
                    value={adjIndex}
                    onChange={(v) => setAdjIndex(v as EconomicIndex)}
                    options={[
                      { value: 'IPCA', label: 'IPCA' },
                      { value: 'INPC', label: 'INPC' },
                      { value: 'IGPM', label: 'IGP-M' },
                    ]}
                  />
                )}
                <FormControlLabel
                  label="não reduzir"
                  sx={{ height: 40, mr: 0 }}
                  control={
                    <Checkbox
                      size="small"
                      checked={adjNoReduction}
                      onChange={(e) => setAdjNoReduction(e.target.checked)}
                      slotProps={{ input: { 'data-cy': 'rec-adjust-no-reduction' } as object }}
                    />
                  }
                />
              </>
            )}
            <Button data-cy="rec-submit" variant="contained" type="submit" disabled={submitting} sx={{ height: 40 }}>
              {submitting ? 'Salvando…' : editing ? 'Salvar alteração' : 'Cadastrar'}
            </Button>
            {editing && (
              <Button data-cy="rec-cancel" variant="outlined" color="inherit" type="button" onClick={resetForm} sx={{ height: 40 }}>
                Cancelar
              </Button>
            )}
          </RowForm>
        </>
      )}

      <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
        Cadastradas
      </Typography>
      {rules.length === 0 ? (
        <Typography color="text.secondary">Nenhuma recorrência.</Typography>
      ) : (
        <TableBox>
          <Table data-cy="rec-table" sx={{ minWidth: 860 }}>
            <TableHead>
              <TableRow>
                <TableCell>Descrição</TableCell>
                <TableCell>Quando</TableCell>
                <TableCell>Conta</TableCell>
                <TableCell align="right">Valor estimado</TableCell>
                <TableCell>Próxima</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {rules.map((r) => {
                const next = r.nextOccurrence;
                const key = next ? `${r.id}:${next.period}` : r.id;
                const adjustment = adjustmentLabel(r);
                return (
                  <Fragment key={r.id}>
                    <TableRow data-cy="rec-row" data-type={r.type} sx={{ verticalAlign: 'top' }}>
                      <TableCell>
                        {r.description}
                        <Sub>{r.category}</Sub>
                      </TableCell>
                      <TableCell>
                        {scheduleLabel(r)}
                        {r.endsOn && <Sub>até {dateLabel(r.endsOn)}</Sub>}
                        {adjustment && <Sub dataCy="rec-adjustment">{adjustment}</Sub>}
                      </TableCell>
                      <TableCell>{accountName(r.accountId)}</TableCell>
                      <TableCell
                        align="right"
                        data-cy="rec-value"
                        sx={{ whiteSpace: 'nowrap', color: r.type === 'Income' ? 'success.main' : 'error.main' }}
                      >
                        {signed(r.type, r.amount, r.currency)}
                        {r.pendingAdjustment && (
                          <TextAction
                            data-cy="rec-adjust-pending"
                            title="Confirmar o reajuste deste ano"
                            onClick={() => setValuesOpen(r.id)}
                            sx={{ display: 'block', ml: 'auto', fontSize: '0.75rem' }}
                          >
                            reajuste de {periodLabel(r.pendingAdjustment.period)} pendente
                          </TextAction>
                        )}
                      </TableCell>
                      <TableCell data-cy="rec-next" sx={{ whiteSpace: 'nowrap' }}>
                        {next ? (
                          dateLabel(next.scheduledOn)
                        ) : (
                          <Box component="span" sx={{ color: 'text.secondary' }}>
                            encerrada
                          </Box>
                        )}
                      </TableCell>
                      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                        {(next && launchForm(key)) || (
                          <>
                            {next && (
                              <>
                                <TextAction
                                  data-cy="rec-launch-next"
                                  title={`Lançar a ocorrência de ${periodLabel(next.period)}`}
                                  onClick={() => openLaunch(r.id, next.period, next.amount, next.scheduledOn)}
                                >
                                  lançar {periodLabel(next.period)}
                                </TextAction>
                                <TextAction data-cy="rec-skip-next" onClick={() => void handleSkip(r.id, next.period, r.description)}>
                                  pular
                                </TextAction>
                              </>
                            )}
                            <TextAction
                              data-cy="rec-values-toggle"
                              aria-expanded={valuesOpen === r.id}
                              onClick={() => setValuesOpen(valuesOpen === r.id ? null : r.id)}
                            >
                              valores
                            </TextAction>
                            <TextAction data-cy="rec-edit" onClick={() => startEdit(r)}>
                              editar
                            </TextAction>
                            <TextAction danger data-cy="rec-remove" onClick={() => void handleRemove(r)}>
                              excluir
                            </TextAction>
                          </>
                        )}
                      </TableCell>
                    </TableRow>
                    {valuesOpen === r.id && (
                      <RecurrenceValuesRow
                        key={`${r.id}:${r.version}`}
                        rule={r}
                        colSpan={6}
                        onChanged={handleValuesChanged}
                        onClose={() => setValuesOpen(null)}
                      />
                    )}
                  </Fragment>
                );
              })}
            </TableBody>
          </Table>
        </TableBox>
      )}
    </section>
  );
}

/** Linha menor dentro da célula (categoria, fim, reajuste). */
function Sub({ children, dataCy }: { children: ReactNode; dataCy?: string }) {
  return (
    <Box component="span" data-cy={dataCy} sx={{ display: 'block', fontSize: '0.75rem', color: 'text.secondary' }}>
      {children}
    </Box>
  );
}
