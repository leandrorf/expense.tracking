import { useEffect, useState, type ReactNode } from 'react';
import ChevronLeft from '@mui/icons-material/ChevronLeft';
import ChevronRight from '@mui/icons-material/ChevronRight';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/client';
import {
  goalsApi,
  type GoalScope,
  type MonthlyGoal,
  type MonthlyGoalHistoryMonth,
  type MonthlyGoalHistoryScope,
  type MonthlyGoalKind,
  type MonthlyGoalScopeStatus,
  type MonthlyGoalSituation,
  type MonthlyGoalStatus,
  type SavingsObjective,
  type SavingsObjectiveSituation,
} from '../api/goals';
import {
  DateField,
  Field,
  MoneyField,
  MonthField,
  Notice,
  ScopeSelect,
  SelectField,
  StatusChip,
  TableBox,
  TextAction,
  type StatusTone,
} from '../ui';

const SCOPES: { scope: GoalScope; label: string; short: string }[] = [
  { scope: 'personal', label: 'Pessoal (PF)', short: 'PF' },
  { scope: 'business', label: 'Empresa (PJ)', short: 'PJ' },
];

const KINDS: { kind: MonthlyGoalKind; label: string }[] = [
  { kind: 'SaveAmount', label: 'Guardar um valor' },
  { kind: 'SavePercent', label: 'Guardar um percentual das entradas' },
  { kind: 'SpendCeiling', label: 'Gastar no máximo (teto)' },
];

/** ADR-0013, regra 5: o mesmo visual de situação dos orçamentos. */
const MONTH_SITUATION: Record<MonthlyGoalSituation, { label: string; icon: string; tone: StatusTone }> = {
  Inside: { label: 'Dentro da meta', icon: '✓', tone: 'ok' },
  Attention: { label: 'Atenção', icon: '!', tone: 'attention' },
  Outside: { label: 'Fora da meta', icon: '✕', tone: 'outside' },
};

/** ADR-0013, regra 7. */
const OBJECTIVE_SITUATION: Record<SavingsObjectiveSituation, { label: string; icon: string; tone: StatusTone }> = {
  OnTrack: { label: 'No ritmo', icon: '✓', tone: 'ok' },
  Behind: { label: 'Atrasado', icon: '!', tone: 'attention' },
  Reached: { label: 'Atingido', icon: '★', tone: 'ok' },
  DeadlinePassed: { label: 'Prazo vencido', icon: '✕', tone: 'outside' },
};

const money = (value: number) => value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

/** Mês corrente no fuso do navegador (`yyyy-MM`, o mesmo do `<input type="month">`). */
function currentMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function today(): string {
  const now = new Date();
  return `${currentMonth()}-${String(now.getDate()).padStart(2, '0')}`;
}

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

/** `2026-11-21` → `21/11/2026`. */
function dateLabel(date: string): string {
  const [year, m, d] = date.split('-');
  return `${d}/${m}/${year}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/** A forma e o valor da meta em uma frase. */
function goalLabel(kind: MonthlyGoalKind, amount: number | null, percent: number | null): string {
  if (kind === 'SavePercent') return `Guardar ${percent ?? 0}% das entradas`;
  if (kind === 'SpendCeiling') return `Gastar no máximo ${money(amount ?? 0)}`;
  return `Guardar ${money(amount ?? 0)}`;
}

/** Aceita "1.234,56", "1234,56" e "1234.56". */
function parseAmount(text: string): number {
  const clean = text.trim();
  return Number(clean.includes(',') ? clean.replace(/\./g, '').replace(',', '.') : clean);
}

/** Título de uma parte da tela. */
function SectionTitle({ children }: { children: ReactNode }) {
  return (
    <Typography variant="h3" component="h3" sx={{ mt: 3, mb: 1.5 }}>
      {children}
    </Typography>
  );
}

/** Um número do cartão: rótulo pequeno e valor. */
function Figure({ label, children, dataCy }: { label: ReactNode; children: ReactNode; dataCy: string }) {
  return (
    <Box>
      <Box component="dt" sx={{ fontSize: '0.75rem', color: 'text.secondary' }}>
        {label}
      </Box>
      <Box component="dd" data-cy={dataCy} sx={{ m: 0, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
        {children}
      </Box>
    </Box>
  );
}

function FigureList({ children }: { children: ReactNode }) {
  return (
    <Box component="dl" sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: 1, m: 0, mb: 1.5 }}>
      {children}
    </Box>
  );
}

/** Selo de situação com o ícone de hoje (✓ ! ✕ ★). */
function SituationChip({ situation, label, dataCy }: { situation: { icon: string; tone: StatusTone }; label: string; dataCy?: string }) {
  return (
    <StatusChip
      tone={situation.tone}
      dataCy={dataCy}
      label={
        <>
          <span aria-hidden="true">{situation.icon}</span> {label}
        </>
      }
    />
  );
}

/** Formulário em linha dentro de um cartão (o `RowForm` do projeto não leva `aria-label`). */
function InlineForm({
  onSubmit,
  dataCy,
  label,
  children,
}: {
  onSubmit: (e: React.FormEvent) => void;
  dataCy: string;
  label: string;
  children: ReactNode;
}) {
  return (
    <Box
      component="form"
      onSubmit={onSubmit}
      data-cy={dataCy}
      aria-label={label}
      sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'flex-start', mb: 1.5, '& > .MuiTextField-root': { flex: '1 1 140px' } }}
    >
      {children}
    </Box>
  );
}

/** MET-7 (ADR-0013): meta do mês de PF e PJ e os objetivos com aportes. */
export function GoalsView() {
  // Muda quando a meta muda, para o histórico buscar de novo.
  const [revision, setRevision] = useState(0);
  return (
    <section>
      <Typography variant="h2" component="h2" sx={{ mb: 1 }}>
        Metas
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        A meta do mês compara o resultado da previsão (entradas − saídas − faturas que vencem no mês) com o que você quer
        guardar ou com um teto de gastos. Os objetivos somam o que você registra como guardado para cada um.
      </Typography>
      <MonthlyGoals revision={revision} onChanged={() => setRevision((r) => r + 1)} />
      <GoalHistory revision={revision} />
      <Objectives onGoalChanged={() => setRevision((r) => r + 1)} />
    </section>
  );
}

/** MET-3/MET-7: situação do mês, "ainda pode gastar" e a meta de cada escopo. */
function MonthlyGoals({ revision: outside, onChanged }: { revision: number; onChanged: () => void }) {
  const [month, setMonth] = useState(currentMonth());
  const [status, setStatus] = useState<MonthlyGoalStatus | null>(null);
  const [goals, setGoals] = useState<MonthlyGoal[]>([]);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    Promise.all([goalsApi.status(month), goalsApi.listMonthly()])
      .then(([s, g]) => {
        if (cancelled) return;
        setStatus(s);
        setGoals(g);
      })
      .catch((err) => !cancelled && setError(messageOf(err)))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [month, revision, outside]);

  const current = currentMonth();
  const shown = status?.month === month ? status : null;

  function changed(text: string) {
    setNotice(text);
    setRevision((r) => r + 1);
    onChanged();
  }

  return (
    <Box data-cy="goal-month" aria-busy={loading}>
      <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1.5 }}>
        <IconButton
          data-cy="goal-month-prev"
          aria-label="Mês anterior"
          disabled={month <= current}
          onClick={() => setMonth((m) => shiftMonth(m, -1))}
        >
          <ChevronLeft />
        </IconButton>
        <Typography variant="h3" component="h3" data-cy="goal-month-label" aria-live="polite">
          Meta de {monthLabel(month)}
        </Typography>
        <IconButton
          data-cy="goal-month-next"
          aria-label="Próximo mês"
          disabled={month >= shiftMonth(current, 11)}
          onClick={() => setMonth((m) => shiftMonth(m, 1))}
        >
          <ChevronRight />
        </IconButton>
        <Button
          variant="outlined"
          color="inherit"
          data-cy="goal-month-refresh"
          onClick={() => setRevision((r) => r + 1)}
          disabled={loading}
        >
          atualizar
        </Button>
      </Stack>
      {shown && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          {shown.notice}
        </Typography>
      )}
      {notice && (
        <Notice severity="success" dataCy="goal-notice">
          {notice}
        </Notice>
      )}
      {error && (
        <Notice severity="error" role="alert">
          {error}
        </Notice>
      )}
      {!shown && loading ? (
        <Typography>Carregando…</Typography>
      ) : (
        shown && (
          <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
            {SCOPES.map(({ scope, label, short }) => (
              <MonthlyScope
                key={scope}
                label={label}
                short={short}
                month={month}
                status={shown[scope]}
                goal={goals.find((g) => g.scope === scope && g.closedFromMonth === null) ?? null}
                onChanged={changed}
              />
            ))}
          </Box>
        )
      )}
    </Box>
  );
}

function MonthlyScope(props: {
  label: string;
  short: string;
  month: string;
  status: MonthlyGoalScopeStatus;
  goal: MonthlyGoal | null;
  onChanged: (notice: string) => void;
}) {
  const { label, short, month, status, goal, onChanged } = props;
  const [editing, setEditing] = useState<'none' | 'set' | 'close'>('none');
  const [kind, setKind] = useState<MonthlyGoalKind>(status.kind ?? 'SaveAmount');
  const [value, setValue] = useState('');
  const [fromMonth, setFromMonth] = useState(month);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function open(mode: 'set' | 'close') {
    setEditing(mode);
    setError(null);
    setKind(status.kind ?? 'SaveAmount');
    setValue(status.kind === 'SavePercent' ? String(status.percent ?? '') : status.amount != null ? String(status.amount) : '');
    setFromMonth(mode === 'close' ? shiftMonth(month, 1) : month);
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editing === 'close') {
        await goalsApi.closeMonthly(status.scope, fromMonth);
        onChanged(`Meta ${short} encerrada a partir de ${monthLabel(fromMonth)}.`);
      } else {
        const number = parseAmount(value);
        await goalsApi.setMonthly(status.scope, {
          kind,
          amount: kind === 'SavePercent' ? null : number,
          percent: kind === 'SavePercent' ? number : null,
          fromMonth,
        });
        onChanged(`Meta ${short}: ${goalLabel(kind, number, number)} a partir de ${monthLabel(fromMonth)}.`);
      }
      setEditing('none');
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSaving(false);
    }
  }

  const situation = status.situation ? MONTH_SITUATION[status.situation] : null;
  const ceiling = status.kind === 'SpendCeiling';

  return (
    <Paper variant="outlined" data-cy={`goal-scope-${status.scope}`} data-situation={status.situation ?? 'none'} sx={{ p: 2 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle1" component="h4" sx={{ fontWeight: 700 }}>
          {label}
        </Typography>
        {situation && <SituationChip situation={situation} label={situation.label} dataCy="goal-situation" />}
      </Stack>
      {status.kind ? (
        <Typography variant="body2" data-cy="goal-kind-label" sx={{ mb: 1.5 }}>
          <strong>{goalLabel(status.kind, status.amount, status.percent)}</strong>
          {status.kind === 'SavePercent' && status.target != null && <> (alvo de {money(status.target)} neste mês)</>}
        </Typography>
      ) : (
        <Typography variant="body2" color="text.secondary" data-cy="goal-kind-label" sx={{ mb: 1.5 }}>
          Sem meta {short} neste mês.
        </Typography>
      )}
      <FigureList>
        <Figure label="Entradas" dataCy="goal-income">
          {money(status.income)}
        </Figure>
        <Figure label="Saídas + faturas" dataCy="goal-outflows">
          {money(status.outflows)}
        </Figure>
        <Figure label="Resultado previsto" dataCy="goal-result">
          {money(status.result)}
        </Figure>
        {status.target != null && (
          <Figure label={ceiling ? 'Teto' : 'Guardar'} dataCy="goal-target">
            {money(status.target)}
          </Figure>
        )}
        {status.canStillSpend != null && (
          <Figure label="Ainda pode gastar" dataCy="goal-can-spend">
            <strong>{money(status.canStillSpend)}</strong>
          </Figure>
        )}
      </FigureList>

      {editing === 'none' ? (
        <Stack direction="row" spacing={1}>
          <Button variant="outlined" color="inherit" data-cy="goal-edit-open" onClick={() => open('set')}>
            {goal ? 'mudar meta' : 'definir meta'}
          </Button>
          {goal && (
            <Button variant="outlined" color="inherit" data-cy="goal-close-open" onClick={() => open('close')}>
              encerrar
            </Button>
          )}
        </Stack>
      ) : (
        <InlineForm onSubmit={submit} dataCy="goal-form" label={`Meta ${short}`}>
          {editing === 'set' && (
            <>
              <SelectField
                dataCy="goal-kind"
                label="Forma da meta"
                value={kind}
                onChange={(v) => setKind(v as MonthlyGoalKind)}
                options={KINDS.map((k) => ({ value: k.kind, label: k.label }))}
              />
              <MoneyField
                dataCy="goal-value"
                label={kind === 'SavePercent' ? 'Percentual das entradas' : 'Valor'}
                placeholder={kind === 'SavePercent' ? '% das entradas' : 'Valor (R$)'}
                value={value}
                onChange={setValue}
                required
                autoFocus
              />
            </>
          )}
          <MonthField
            dataCy="goal-from"
            label={editing === 'set' ? 'a partir de' : 'deixa de valer a partir de'}
            value={fromMonth}
            onChange={setFromMonth}
            required
          />
          <Button data-cy="goal-save" variant="contained" type="submit" disabled={saving} sx={{ height: 40 }}>
            {saving ? 'Salvando…' : editing === 'close' ? 'Encerrar' : 'Salvar'}
          </Button>
          <Button variant="outlined" color="inherit" type="button" onClick={() => setEditing('none')} disabled={saving} sx={{ height: 40 }}>
            Cancelar
          </Button>
        </InlineForm>
      )}
      {error && (
        <Notice severity="error" role="alert" dataCy="goal-error" sx={{ mt: 1, mb: 0 }}>
          {error}
        </Notice>
      )}
    </Paper>
  );
}

/** MET-4: os últimos 6 meses fechados, com a meta que valia e se foi atingida. */
function GoalHistory({ revision }: { revision: number }) {
  const [months, setMonths] = useState<MonthlyGoalHistoryMonth[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    goalsApi
      .history(6)
      .then((list) => !cancelled && setMonths(list))
      .catch((err) => !cancelled && setError(messageOf(err)));
    return () => {
      cancelled = true;
    };
  }, [revision]);

  if (error) {
    return (
      <Notice severity="error" role="alert">
        {error}
      </Notice>
    );
  }

  if (!months.some((m) => m.personal.achieved !== null || m.business.achieved !== null)) {
    return null;
  }

  const cell = (h: MonthlyGoalHistoryScope) =>
    h.achieved === null ? (
      <TableCell data-cy={`history-${h.scope}`} data-achieved="none" sx={{ color: 'text.secondary' }}>
        sem meta
      </TableCell>
    ) : (
      <TableCell data-cy={`history-${h.scope}`} data-achieved={h.achieved}>
        <SituationChip
          situation={{ icon: h.achieved ? '✓' : '✕', tone: h.achieved ? 'ok' : 'outside' }}
          label={h.achieved ? 'Atingida' : 'Não atingida'}
        />{' '}
        {h.kind === 'SpendCeiling'
          ? `gastou ${money(h.outflows)} de ${money(h.target ?? 0)}`
          : `guardou ${money(h.result)} de ${money(h.target ?? 0)}`}
      </TableCell>
    );

  return (
    <Box data-cy="goal-history" sx={{ mt: 2 }}>
      <Typography variant="subtitle1" component="h4" sx={{ fontWeight: 700, mb: 1 }}>
        Últimos meses
      </Typography>
      <TableBox>
        <Table sx={{ minWidth: 600 }}>
          <TableHead>
            <TableRow>
              <TableCell>Mês</TableCell>
              <TableCell>Pessoal (PF)</TableCell>
              <TableCell>Empresa (PJ)</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {[...months].reverse().map((m) => (
              <TableRow key={m.month} data-cy="goal-history-row" data-month={m.month}>
                <TableCell>{monthLabel(m.month)}</TableCell>
                {cell(m.personal)}
                {cell(m.business)}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableBox>
    </Box>
  );
}

/** MET-5/MET-6/MET-7: objetivos com aportes, ritmo e situação. */
function Objectives({ onGoalChanged }: { onGoalChanged: () => void }) {
  const [objectives, setObjectives] = useState<SavingsObjective[]>([]);
  const [showArchived, setShowArchived] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [scope, setScope] = useState<GoalScope>('personal');
  const [name, setName] = useState('');
  const [target, setTarget] = useState('');
  const [deadline, setDeadline] = useState('');
  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [goalNotice, setGoalNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    goalsApi
      .listObjectives(showArchived)
      .then((list) => !cancelled && setObjectives(list))
      .catch((err) => !cancelled && setError(messageOf(err)))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [showArchived]);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setCreating(true);
    setFormError(null);
    try {
      const created = await goalsApi.createObjective(scope, { name, target: parseAmount(target), deadline: deadline || null });
      setObjectives((list) => [...list, created]);
      setName('');
      setTarget('');
      setDeadline('');
    } catch (err) {
      setFormError(messageOf(err));
    } finally {
      setCreating(false);
    }
  }

  /**
   * MET-10 (ADR-0013, decisão 8): soma do "precisa por mês" dos objetivos em andamento com prazo, por escopo. A
   * meta mensal e os objetivos continuam independentes: o botão só copia a soma para a meta, a partir deste mês.
   */
  const needed = SCOPES.map(({ scope, short }) => ({
    scope,
    short,
    total:
      Math.round(
        objectives
          .filter((o) => o.scope === scope && !o.archived && o.neededPerMonth !== null)
          .reduce((sum, o) => sum + (o.neededPerMonth ?? 0), 0) * 100,
      ) / 100,
  })).filter((n) => n.total > 0);

  async function applyAsGoal(scope: GoalScope, short: string, total: number) {
    setError(null);
    try {
      await goalsApi.setMonthly(scope, { kind: 'SaveAmount', amount: total, fromMonth: currentMonth() });
      setGoalNotice(`Meta ${short}: guardar ${money(total)} a partir de ${monthLabel(currentMonth())}.`);
      onGoalChanged();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  function replace(updated: SavingsObjective) {
    setObjectives((list) =>
      updated.archived && !showArchived ? list.filter((o) => o.id !== updated.id) : list.map((o) => (o.id === updated.id ? updated : o)),
    );
  }

  return (
    <Box data-cy="objectives" data-loading={loading}>
      <SectionTitle>Objetivos</SectionTitle>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Registre o que você guardou para cada objetivo (uma retirada é um valor negativo). O que você registra aqui não é
        lançamento: não aparece no Resumo, nos orçamentos nem na previsão.
      </Typography>
      <InlineForm onSubmit={create} dataCy="objective-form" label="Novo objetivo">
        <ScopeSelect dataCy="objective-scope" personal="personal" business="business" value={scope} onChange={(v) => setScope(v as GoalScope)} />
        <Field
          dataCy="objective-name"
          label="Nome do objetivo"
          placeholder="Ex.: Reserva de emergência"
          value={name}
          onChange={setName}
          required
          slotProps={{ htmlInput: { maxLength: 60 } }}
        />
        <MoneyField dataCy="objective-target" label="Alvo" placeholder="Alvo (R$)" value={target} onChange={setTarget} required />
        <MonthField dataCy="objective-deadline" label="prazo (opcional)" value={deadline} onChange={setDeadline} />
        <Button data-cy="objective-submit" variant="contained" type="submit" disabled={creating} sx={{ height: 40 }}>
          {creating ? 'Criando…' : 'Criar objetivo'}
        </Button>
      </InlineForm>
      {formError && (
        <Notice severity="error" role="alert" dataCy="objective-form-error">
          {formError}
        </Notice>
      )}
      {needed.length > 0 && (
        <Paper variant="outlined" data-cy="objectives-needed" sx={{ p: 1.5, mb: 2 }}>
          {needed.map((n) => (
            <Stack
              key={n.scope}
              direction="row"
              spacing={1}
              useFlexGap
              data-cy={`objectives-needed-${n.scope}`}
              data-total={n.total}
              sx={{ alignItems: 'center', flexWrap: 'wrap', py: 0.5 }}
            >
              <Typography variant="body2">
                Para chegar no prazo, os objetivos {n.short} precisam de <strong>{money(n.total)}</strong> por mês.
              </Typography>
              <Button
                variant="outlined"
                color="inherit"
                data-cy={`objectives-use-as-goal-${n.scope}`}
                onClick={() => void applyAsGoal(n.scope, n.short, n.total)}
              >
                usar como meta {n.short}
              </Button>
            </Stack>
          ))}
        </Paper>
      )}
      {goalNotice && (
        <Notice severity="success" dataCy="objectives-goal-notice">
          {goalNotice}
        </Notice>
      )}
      <FormControlLabel
        sx={{ mb: 1 }}
        control={
          <Checkbox
            size="small"
            checked={showArchived}
            onChange={(e) => setShowArchived(e.target.checked)}
            slotProps={{ input: { 'data-cy': 'objectives-show-archived' } as object }}
          />
        }
        label="mostrar arquivados"
      />
      {error && (
        <Notice severity="error" role="alert">
          {error}
        </Notice>
      )}
      {loading && objectives.length === 0 ? (
        <Typography>Carregando…</Typography>
      ) : objectives.length === 0 ? (
        <Typography color="text.secondary" data-cy="objectives-empty">
          Nenhum objetivo ainda.
        </Typography>
      ) : (
        <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
          {objectives.map((o) => (
            <ObjectiveItem key={o.id} objective={o} onChanged={replace} />
          ))}
        </Box>
      )}
    </Box>
  );
}

function ObjectiveItem({ objective: o, onChanged }: { objective: SavingsObjective; onChanged: (o: SavingsObjective) => void }) {
  const [panel, setPanel] = useState<'none' | 'contribute' | 'edit'>('none');
  const [direction, setDirection] = useState<'save' | 'withdraw'>('save');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(today());
  const [note, setNote] = useState('');
  const [name, setName] = useState(o.name);
  const [target, setTarget] = useState(String(o.target));
  const [deadline, setDeadline] = useState(o.deadline ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<SavingsObjective>, after?: () => void) {
    setBusy(true);
    setError(null);
    try {
      onChanged(await action());
      after?.();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  function contribute(e: React.FormEvent) {
    e.preventDefault();
    const value = parseAmount(amount);
    void run(
      () => goalsApi.contribute(o.id, date, direction === 'withdraw' ? -value : value, note.trim() || null),
      () => {
        setAmount('');
        setNote('');
        setPanel('none');
      },
    );
  }

  function edit(e: React.FormEvent) {
    e.preventDefault();
    void run(
      () => goalsApi.editObjective(o.id, { name, target: parseAmount(target), deadline: deadline || null }),
      () => setPanel('none'),
    );
  }

  function openEdit() {
    setName(o.name);
    setTarget(String(o.target));
    setDeadline(o.deadline ?? '');
    setError(null);
    setPanel('edit');
  }

  const situation = OBJECTIVE_SITUATION[o.situation];
  const percent = o.target > 0 ? Math.min(100, (o.saved / o.target) * 100) : 0;
  const scopeShort = o.scope === 'business' ? 'PJ' : 'PF';

  return (
    <Paper
      component="li"
      variant="outlined"
      data-cy="objective-row"
      data-name={o.name}
      data-situation={o.situation}
      data-archived={o.archived}
      sx={{ p: 2, opacity: o.archived ? 0.7 : 1 }}
    >
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <strong>
          {o.name}{' '}
          <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}>
            ({scopeShort})
          </Box>
        </strong>
        <SituationChip situation={situation} label={o.archived ? 'Arquivado' : situation.label} dataCy="objective-situation" />
      </Stack>
      <LinearProgress
        variant="determinate"
        color={situation.tone === 'outside' ? 'error' : situation.tone === 'attention' ? 'warning' : 'success'}
        value={percent}
        aria-label={`Guardado de ${o.name}`}
        sx={{ height: 8, borderRadius: 4, mb: 1.5 }}
      />
      <FigureList>
        <Figure label="Guardado" dataCy="objective-saved">
          {money(o.saved)} de {money(o.target)}
        </Figure>
        <Figure label="Falta" dataCy="objective-missing">
          {money(o.missing)}
        </Figure>
        <Figure label="Prazo" dataCy="objective-deadline-label">
          {o.deadline ? monthLabel(o.deadline) : 'sem prazo'}
        </Figure>
        {o.neededPerMonth != null && (
          <Figure label="Precisa por mês" dataCy="objective-needed">
            {money(o.neededPerMonth)}
          </Figure>
        )}
        <Figure label="Ritmo (últimos 3 meses)" dataCy="objective-pace">
          {money(o.pace)}/mês
        </Figure>
        <Figure label="Chegada prevista" dataCy="objective-arrival">
          {o.expectedArrival ? monthLabel(o.expectedArrival) : o.situation === 'Reached' ? 'já chegou' : '—'}
        </Figure>
      </FigureList>

      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 1 }}>
        {o.archived ? (
          <Button
            variant="outlined"
            color="inherit"
            data-cy="objective-unarchive"
            disabled={busy}
            onClick={() => void run(() => goalsApi.unarchiveObjective(o.id))}
          >
            reabrir
          </Button>
        ) : (
          <>
            <Button
              variant="outlined"
              color="inherit"
              data-cy="contribution-open"
              onClick={() => setPanel(panel === 'contribute' ? 'none' : 'contribute')}
            >
              registrar aporte
            </Button>
            <Button variant="outlined" color="inherit" data-cy="objective-edit-open" onClick={openEdit}>
              editar
            </Button>
            <Button
              variant="outlined"
              color="inherit"
              data-cy="objective-archive"
              disabled={busy}
              onClick={() => void run(() => goalsApi.archiveObjective(o.id))}
            >
              arquivar
            </Button>
          </>
        )}
      </Stack>

      {panel === 'contribute' && (
        <InlineForm onSubmit={contribute} dataCy="contribution-form" label={`Aporte em ${o.name}`}>
          <SelectField
            dataCy="contribution-direction"
            label="Guardar ou retirar"
            value={direction}
            onChange={(v) => setDirection(v as 'save' | 'withdraw')}
            options={[
              { value: 'save', label: 'Guardei' },
              { value: 'withdraw', label: 'Retirei' },
            ]}
          />
          <MoneyField
            dataCy="contribution-amount"
            label="Valor do aporte"
            placeholder="Valor (R$)"
            value={amount}
            onChange={setAmount}
            required
            autoFocus
          />
          <DateField dataCy="contribution-date" label="Data" value={date} onChange={setDate} required slotProps={{ htmlInput: { max: today() } }} />
          <Field
            dataCy="contribution-note"
            label="Observação"
            placeholder="Observação (opcional)"
            value={note}
            onChange={setNote}
            slotProps={{ htmlInput: { maxLength: 120 } }}
          />
          <Button data-cy="contribution-submit" variant="contained" type="submit" disabled={busy} sx={{ height: 40 }}>
            {busy ? 'Salvando…' : 'Registrar'}
          </Button>
        </InlineForm>
      )}

      {panel === 'edit' && (
        <InlineForm onSubmit={edit} dataCy="objective-edit-form" label={`Editar ${o.name}`}>
          <Field dataCy="objective-edit-name" label="Nome" value={name} onChange={setName} required slotProps={{ htmlInput: { maxLength: 60 } }} />
          <MoneyField dataCy="objective-edit-target" label="Alvo" value={target} onChange={setTarget} required />
          <MonthField dataCy="objective-edit-deadline" label="prazo" value={deadline} onChange={setDeadline} />
          <Button data-cy="objective-edit-save" variant="contained" type="submit" disabled={busy} sx={{ height: 40 }}>
            Salvar
          </Button>
          <Button variant="outlined" color="inherit" type="button" onClick={() => setPanel('none')} disabled={busy} sx={{ height: 40 }}>
            Cancelar
          </Button>
        </InlineForm>
      )}

      {error && (
        <Notice severity="error" role="alert" dataCy="objective-error" sx={{ mb: 1 }}>
          {error}
        </Notice>
      )}

      {o.contributions.length > 0 && (
        <Box component="details" sx={{ fontSize: '0.875rem', '& summary': { cursor: 'pointer', color: 'text.secondary' } }}>
          <summary data-cy="contributions-toggle">Aportes ({o.contributions.length})</summary>
          <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, mt: 1 }}>
            {o.contributions.map((c) => (
              <Box
                component="li"
                key={c.id}
                data-cy="contribution-row"
                data-amount={c.amount}
                sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1.5, py: 0.25 }}
              >
                <span>{dateLabel(c.date)}</span>
                <Box component="span" sx={{ fontVariantNumeric: 'tabular-nums', color: c.amount < 0 ? 'error.main' : undefined }}>
                  {money(c.amount)}
                </Box>
                {c.note && (
                  <Box component="span" sx={{ color: 'text.secondary' }}>
                    {c.note}
                  </Box>
                )}
                {!o.archived && (
                  <TextAction
                    type="button"
                    data-cy="contribution-delete"
                    disabled={busy}
                    onClick={() => void run(() => goalsApi.removeContribution(o.id, c.id))}
                  >
                    excluir
                  </TextAction>
                )}
              </Box>
            ))}
          </Box>
        </Box>
      )}
    </Paper>
  );
}
