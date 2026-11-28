import { useEffect, useState, type ReactNode } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { budgetsApi, type Budget, type BudgetHealth, type BudgetMonth, type BudgetScope } from '../api/budgets';
import { ApiError } from '../api/client';
import { Field, MonthField, MonthNav, Notice, ScopeSelect, StatusChip, TableBox, TextAction, type StatusTone } from '../ui';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';

/** ADR-0002: limiar de atenção padrão, o mesmo do reporting. */
const DEFAULT_WARNING_THRESHOLD = 80;

const SCOPES: { scope: BudgetScope; label: string }[] = [
  { scope: 'Personal', label: 'Pessoal (PF)' },
  { scope: 'Business', label: 'Empresa (PJ)' },
];

/** Formulário em linha: os campos quebram de linha no celular. */
const FORM_SX = {
  display: 'flex',
  flexWrap: 'wrap',
  gap: 1,
  alignItems: 'flex-start',
  mb: 2,
  '& > .MuiTextField-root': { flex: '1 1 140px' },
} as const;

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

/** Título de uma parte da tela. */
function SectionTitle({ children }: { children: ReactNode }) {
  return (
    <Typography variant="h3" component="h3" sx={{ mt: 3, mb: 1.5 }}>
      {children}
    </Typography>
  );
}

/** BDG-5/6: acompanhamento do mês, cadastro, alteração de valor (a partir de um mês) e encerramento. */
export function BudgetsView() {
  const [budgets, setBudgets] = useState<Budget[]>([]);
  // Muda a cada alteração nos orçamentos, para o acompanhamento do mês buscar de novo.
  const [revision, setRevision] = useState(0);
  const { categories, remember } = useCategorySuggestions();
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [category, setCategory] = useState('');
  const [scope, setScope] = useState<BudgetScope>('Personal');
  const [amount, setAmount] = useState('');
  const [startMonth, setStartMonth] = useState(currentMonth());
  const [warningThreshold, setWarningThreshold] = useState(String(DEFAULT_WARNING_THRESHOLD));
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
      const created = await budgetsApi.create({
        category,
        scope,
        amount: Number(amount),
        startMonth,
        warningThreshold: Number(warningThreshold),
      });
      setBudgets((current) => [...current, created]);
      remember(created.category);
      setRevision((r) => r + 1);
      setCategory('');
      setAmount('');
      setWarningThreshold(String(DEFAULT_WARNING_THRESHOLD));
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
      <Typography variant="h2" component="h2" sx={{ mb: 1 }}>
        Orçamentos
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Limite mensal de gasto por categoria, separado para PF e PJ. A categoria é comparada sem diferença de maiúsculas
        e acentos.
      </Typography>

      <MonthTracking revision={revision} />

      <SectionTitle>Cadastro</SectionTitle>
      <Box component="form" onSubmit={handleCreate} data-cy="budget-form" aria-label="Novo orçamento" sx={FORM_SX}>
        <Field
          dataCy="budget-category"
          label="Categoria"
          value={category}
          onChange={setCategory}
          required
          slotProps={{ htmlInput: { list: 'budget-category-suggestions', autoComplete: 'off', maxLength: 60 } }}
        />
        <CategoryDatalist id="budget-category-suggestions" categories={categories} />
        <ScopeSelect dataCy="budget-scope" value={scope} onChange={(v) => setScope(v as BudgetScope)} />
        <Field
          dataCy="budget-amount"
          label="Valor mensal"
          type="number"
          value={amount}
          onChange={setAmount}
          required
          slotProps={{ htmlInput: { step: '0.01', min: '0.01' } }}
        />
        <MonthField dataCy="budget-start-month" label="Vale a partir de" value={startMonth} onChange={setStartMonth} required />
        <Field
          dataCy="budget-warning-threshold"
          label="Atenção a partir de (%)"
          type="number"
          value={warningThreshold}
          onChange={setWarningThreshold}
          required
          slotProps={{ htmlInput: { min: '1', max: '100', step: '1' } }}
        />
        <Button data-cy="budget-submit" variant="contained" type="submit" disabled={submitting} sx={{ height: 40 }}>
          {submitting ? 'Salvando…' : 'Adicionar'}
        </Button>
      </Box>
      {formError && (
        <Notice severity="error" role="alert" dataCy="budget-form-error">
          {formError}
        </Notice>
      )}

      {loadError && <Notice severity="error">{loadError}</Notice>}
      {loading ? (
        <Typography>Carregando…</Typography>
      ) : (
        SCOPES.map(({ scope: groupScope, label }) => {
          const group = budgets
            .filter((b) => b.scope === groupScope)
            .sort((a, b) => a.category.localeCompare(b.category, 'pt-BR') || a.startMonth.localeCompare(b.startMonth));
          return (
            <Box key={groupScope} data-cy={`budgets-${groupScope.toLowerCase()}`} sx={{ mb: 2 }}>
              <Typography variant="subtitle1" component="h3" sx={{ fontWeight: 700, mb: 1 }}>
                {label}
              </Typography>
              {group.length === 0 ? (
                <Typography color="text.secondary">Nenhum orçamento {groupScope === 'Personal' ? 'PF' : 'PJ'}.</Typography>
              ) : (
                <TableBox>
                  <Table sx={{ minWidth: 720 }}>
                    <TableHead>
                      <TableRow>
                        <TableCell>Categoria</TableCell>
                        <TableCell align="right">Valor atual</TableCell>
                        <TableCell>Vigências</TableCell>
                        <TableCell align="right">Atenção</TableCell>
                        <TableCell>Situação</TableCell>
                        <TableCell />
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {group.map((b) => (
                        <BudgetRow key={b.id} budget={b} onChanged={replace} />
                      ))}
                    </TableBody>
                  </Table>
                </TableBox>
              )}
            </Box>
          );
        })
      )}
    </section>
  );
}

type Editing = 'none' | 'amount' | 'threshold' | 'close';

const FORM_CY: Record<Exclude<Editing, 'none'>, string> = {
  amount: 'budget-change-form',
  threshold: 'budget-threshold-form',
  close: 'budget-close-form',
};

function BudgetRow({ budget, onChanged }: { budget: Budget; onChanged: (b: Budget) => void }) {
  const [editing, setEditing] = useState<Editing>('none');
  const [amount, setAmount] = useState(String(budget.amount));
  const [month, setMonth] = useState(currentMonth());
  const [threshold, setThreshold] = useState(String(budget.warningThreshold));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function open(mode: Editing) {
    setEditing(mode);
    setAmount(String(budget.amount));
    setMonth(currentMonth());
    setThreshold(String(budget.warningThreshold));
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
          : editing === 'threshold'
            ? await budgetsApi.changeWarningThreshold(budget.id, Number(threshold))
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
      <TableRow data-cy="budget-row" data-category={budget.category} sx={{ opacity: closed ? 0.5 : 1 }}>
        <TableCell>{budget.category}</TableCell>
        <TableCell align="right" data-cy="budget-row-amount">
          {money(budget.amount, budget.currency)}
        </TableCell>
        <TableCell sx={{ color: 'text.secondary' }}>
          {budget.limits.map((l) => `${monthLabel(l.fromMonth)}: ${money(l.amount, budget.currency)}`).join(' → ')}
        </TableCell>
        <TableCell align="right" data-cy="budget-row-threshold">
          {budget.warningThreshold}%
        </TableCell>
        <TableCell data-cy="budget-row-state">
          {closed ? `Encerrado a partir de ${monthLabel(budget.closedFromMonth!)}` : `Desde ${monthLabel(budget.startMonth)}`}
        </TableCell>
        <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
          <TextAction data-cy="budget-change" onClick={() => open('amount')} disabled={editing !== 'none'}>
            alterar valor
          </TextAction>
          <TextAction data-cy="budget-change-threshold" onClick={() => open('threshold')} disabled={editing !== 'none'}>
            alterar limiar
          </TextAction>
          {!closed && (
            <TextAction danger data-cy="budget-close" onClick={() => open('close')} disabled={editing !== 'none'}>
              encerrar
            </TextAction>
          )}
        </TableCell>
      </TableRow>
      {editing !== 'none' && (
        <TableRow>
          <TableCell colSpan={6} sx={{ bgcolor: 'background.default' }}>
            <Box
              component="form"
              onSubmit={handleSubmit}
              data-cy={FORM_CY[editing]}
              aria-label={
                editing === 'amount'
                  ? `Alterar valor de ${budget.category}`
                  : editing === 'threshold'
                    ? `Alterar limiar de atenção de ${budget.category}`
                    : `Encerrar ${budget.category}`
              }
              sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'flex-start' }}
            >
              {editing === 'amount' && (
                <Field
                  dataCy="budget-change-amount"
                  label="Novo valor"
                  type="number"
                  value={amount}
                  onChange={setAmount}
                  required
                  autoFocus
                  slotProps={{ htmlInput: { step: '0.01', min: '0.01' } }}
                  sx={{ width: 160 }}
                />
              )}
              {editing === 'threshold' ? (
                <Field
                  dataCy="budget-threshold-value"
                  label="Atenção a partir de (%)"
                  helperText="vale para todos os meses"
                  type="number"
                  value={threshold}
                  onChange={setThreshold}
                  required
                  autoFocus
                  slotProps={{ htmlInput: { min: '1', max: '100', step: '1' } }}
                  sx={{ width: 200 }}
                />
              ) : (
                <MonthField
                  dataCy="budget-edit-month"
                  label={editing === 'amount' ? 'a partir de' : 'deixa de valer a partir de'}
                  value={month}
                  onChange={setMonth}
                  required
                  autoFocus={editing === 'close'}
                  sx={{ width: 220 }}
                />
              )}
              <Button data-cy="budget-edit-submit" variant="contained" type="submit" disabled={saving} sx={{ height: 40 }}>
                {saving ? 'Salvando…' : editing === 'close' ? 'Encerrar' : 'Salvar'}
              </Button>
              <Button
                variant="outlined"
                color="inherit"
                type="button"
                onClick={() => setEditing('none')}
                disabled={saving}
                sx={{ height: 40 }}
              >
                Cancelar
              </Button>
            </Box>
            {error && (
              <Notice severity="error" role="alert" dataCy="budget-edit-error" sx={{ mt: 1, mb: 0 }}>
                {error}
              </Notice>
            )}
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

const HEALTH: Record<BudgetHealth, { label: string; icon: string; tone: StatusTone; bar: 'success' | 'warning' | 'error' }> = {
  OnTrack: { label: 'Dentro', icon: '✓', tone: 'ok', bar: 'success' },
  Warning: { label: 'Atenção', icon: '!', tone: 'attention', bar: 'warning' },
  Exceeded: { label: 'Estourado', icon: '✕', tone: 'outside', bar: 'error' },
};

const percentLabel = (value: number) => `${value.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%`;

/** Um número do acompanhamento: rótulo pequeno e valor. */
function Figure({ label, value, dataCy }: { label: ReactNode; value: ReactNode; dataCy: string }) {
  return (
    <Box>
      <Box component="dt" sx={{ fontSize: '0.75rem', color: 'text.secondary' }}>
        {label}
      </Box>
      <Box component="dd" data-cy={dataCy} sx={{ m: 0, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </Box>
    </Box>
  );
}

/**
 * BDG-6/8: limite, consumido, restante e situação de cada orçamento num mês, com navegação entre meses,
 * e o que foi gasto sem orçamento em cada escopo.
 */
function MonthTracking({ revision }: { revision: number }) {
  const [month, setMonth] = useState(currentMonth());
  // O consumido vem da projeção, que chega alguns segundos depois do lançamento: "atualizar" busca de novo.
  const [refresh, setRefresh] = useState(0);
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
  }, [month, revision, refresh]);

  // Descarta resposta de outro mês enquanto a nova não chega.
  const statuses = data?.month === month ? data.budgets : [];
  const unbudgeted = data?.month === month ? data.unbudgeted : [];

  return (
    <Box data-cy="budget-month" aria-busy={loading}>
      <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1 }}>
        <MonthNav
          dataCy="budget-month"
          label={`Acompanhamento de ${monthLabel(month)}`}
          onPrev={() => setMonth((m) => shiftMonth(m, -1))}
          onNext={() => setMonth((m) => shiftMonth(m, 1))}
        />
        <Button
          variant="outlined"
          color="inherit"
          data-cy="budget-month-refresh"
          onClick={() => setRefresh((r) => r + 1)}
          disabled={loading}
          sx={{ mb: 1.5 }}
        >
          atualizar
        </Button>
        {month !== currentMonth() && (
          <Button
            variant="outlined"
            color="inherit"
            data-cy="budget-month-current"
            onClick={() => setMonth(currentMonth())}
            sx={{ mb: 1.5 }}
          >
            mês atual
          </Button>
        )}
      </Stack>

      {error && (
        <Notice severity="error" role="alert">
          {error}
        </Notice>
      )}
      {loading && statuses.length === 0 ? (
        <Typography>Carregando…</Typography>
      ) : (
        SCOPES.map(({ scope, label }) => {
          const group = statuses.filter((s) => s.scope === scope);
          const withoutBudget = unbudgeted.filter((u) => u.scope === scope);
          return (
            <Box key={scope} data-cy={`budget-status-${scope.toLowerCase()}`} sx={{ mb: 2 }}>
              <Typography variant="subtitle1" component="h4" sx={{ fontWeight: 700, mb: 1 }}>
                {label}
              </Typography>
              {group.length === 0 ? (
                <Typography color="text.secondary">
                  Nenhum orçamento {scope === 'Personal' ? 'PF' : 'PJ'} vale neste mês.
                </Typography>
              ) : (
                <Box
                  component="ul"
                  sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}
                >
                  {group.map((s) => {
                    const health = HEALTH[s.status];
                    return (
                      <Paper
                        component="li"
                        variant="outlined"
                        key={s.budgetId}
                        data-cy="budget-status-row"
                        data-category={s.category}
                        data-status={s.status}
                        sx={{ p: 2 }}
                      >
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
                          <strong>{s.category}</strong>
                          <StatusChip
                            tone={health.tone}
                            dataCy="budget-status-health"
                            label={
                              <>
                                <span aria-hidden="true">{health.icon}</span> {health.label}
                              </>
                            }
                          />
                        </Stack>
                        <Box sx={{ position: 'relative', mb: 1.5 }}>
                          <LinearProgress
                            variant="determinate"
                            color={health.bar}
                            value={Math.min(100, s.percent)}
                            aria-label={`Consumido de ${s.category}`}
                            aria-valuetext={percentLabel(s.percent)}
                            sx={{ height: 8, borderRadius: 4 }}
                          />
                          <Box
                            title={`Atenção a partir de ${s.warningThreshold}%`}
                            sx={{
                              position: 'absolute',
                              top: -3,
                              bottom: -3,
                              width: 2,
                              bgcolor: 'text.secondary',
                              left: `${s.warningThreshold}%`,
                            }}
                          />
                        </Box>
                        <Box
                          component="dl"
                          sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(110px, 1fr))', gap: 1, m: 0 }}
                        >
                          <Figure label="Limite" value={money(s.limit, s.currency)} dataCy="budget-status-limit" />
                          <Figure label="Consumido" value={money(s.spent, s.currency)} dataCy="budget-status-spent" />
                          <Figure
                            label={s.remaining < 0 ? 'Passou' : 'Restante'}
                            value={money(Math.abs(s.remaining), s.currency)}
                            dataCy="budget-status-remaining"
                          />
                          <Figure
                            label={`Uso (atenção em ${s.warningThreshold}%)`}
                            value={percentLabel(s.percent)}
                            dataCy="budget-status-percent"
                          />
                        </Box>
                      </Paper>
                    );
                  })}
                </Box>
              )}
              {withoutBudget.map((u) => (
                <Paper key={u.currency} variant="outlined" data-cy="budget-unbudgeted" data-currency={u.currency} sx={{ p: 2, mt: 1.5 }}>
                  <Stack direction="row" sx={{ justifyContent: 'space-between', mb: 0.5 }}>
                    <strong>Sem orçamento</strong>
                    <Box component="span" data-cy="budget-unbudgeted-total" sx={{ fontVariantNumeric: 'tabular-nums' }}>
                      {money(u.total, u.currency)}
                    </Box>
                  </Stack>
                  <Box
                    component="ul"
                    aria-label={`Gastos ${scope === 'Personal' ? 'PF' : 'PJ'} sem orçamento`}
                    sx={{ listStyle: 'none', p: 0, m: 0 }}
                  >
                    {u.categories.map((c) => (
                      <Box
                        component="li"
                        key={c.category}
                        data-cy="budget-unbudgeted-category"
                        data-category={c.category}
                        sx={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.875rem', color: 'text.secondary', py: 0.25 }}
                      >
                        <span>{c.category}</span>
                        <span>{money(c.total, u.currency)}</span>
                      </Box>
                    ))}
                  </Box>
                </Paper>
              ))}
            </Box>
          );
        })
      )}
    </Box>
  );
}
