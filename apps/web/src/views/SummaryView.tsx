import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/client';
import { goalsApi, type MonthlyGoalScopeStatus, type MonthlyGoalSituation, type MonthlyGoalStatus } from '../api/goals';
import { reportsApi, type ReportSummary, type TotalByKey } from '../api/reports';
import { DateField, Notice, Stat, StatRow, StatusChip, type StatusTone } from '../ui';

const SCOPE_LABEL: Record<string, string> = {
  Personal: 'Pessoal (PF)',
  Business: 'Empresa (PJ)',
  Unknown: 'Desconhecido',
};

/** MET-8: o mesmo selo da aba Metas. */
const GOAL_SITUATION: Record<MonthlyGoalSituation, { label: string; tone: StatusTone }> = {
  Inside: { label: 'Dentro da meta', tone: 'ok' },
  Attention: { label: 'Atenção', tone: 'attention' },
  Outside: { label: 'Fora da meta', tone: 'outside' },
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
      <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
        Resumo
      </Typography>

      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mb: 2 }}>
        <DateField label="De" value={from} onChange={setFrom} sx={{ width: 170 }} />
        <DateField label="Até" value={to} onChange={setTo} sx={{ width: 170 }} />
        <Button variant="contained" onClick={() => void load()} data-cy="summary-apply">
          Aplicar
        </Button>
      </Stack>

      {error && <Notice severity="error">{error}</Notice>}
      {loading ? (
        <Typography>Carregando…</Typography>
      ) : summary ? (
        <>
          <StatRow dataCy="summary-stats">
            <Stat label="Gastos" value={money(summary.totalExpense, summary.currency)} tone="expense" dataCy="stat-expense" />
            <Stat label="Receitas" value={money(summary.totalIncome, summary.currency)} tone="income" dataCy="stat-income" />
            <Stat label="Saldo" value={money(summary.net, summary.currency)} tone={summary.net >= 0 ? 'income' : 'expense'} dataCy="stat-net" />
            <Stat label="Lançamentos" value={String(summary.count)} dataCy="stat-count" />
          </StatRow>

          {goal && (goal.personal.situation || goal.business.situation) && (
            <Box data-cy="summary-goal" sx={{ mb: 3 }}>
              <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
                Meta do mês
              </Typography>
              <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gap: 1 }}>
                {[goal.personal, goal.business]
                  .filter((s) => s.situation)
                  .map((s) => (
                    <GoalLine key={s.scope} status={s} money={(v) => money(v, 'BRL')} />
                  ))}
              </Box>
              {onOpenGoals && (
                <Button variant="text" data-cy="summary-goal-open" onClick={onOpenGoals} sx={{ mt: 1, px: 0 }}>
                  ver metas
                </Button>
              )}
            </Box>
          )}

          <Grid container spacing={1.5}>
            <Breakdown title="Por categoria" items={summary.byCategory} currency={summary.currency} money={money} cy="by-category" />
            <Breakdown
              title="Por escopo (PF/PJ)"
              items={summary.byScope.map((s) => ({ key: SCOPE_LABEL[s.key] ?? s.key, total: s.total }))}
              currency={summary.currency}
              money={money}
              cy="by-scope"
            />
            <Breakdown title="Por meio de pagamento" items={summary.byPaymentMethod} currency={summary.currency} money={money} cy="by-payment-method" />
          </Grid>
        </>
      ) : null}
    </section>
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
    <Grid size={{ xs: 12, md: 4 }}>
      <Paper variant="outlined" data-cy={cy} sx={{ p: 2, height: '100%' }}>
        <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
          {title}
        </Typography>
        {items.length === 0 ? (
          <Typography color="text.secondary">—</Typography>
        ) : (
          <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {items.map((i) => (
              <Box
                component="li"
                key={i.key}
                sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, py: 0.75, fontSize: '0.9rem' }}
              >
                <span>{i.key}</span>
                <Box component="strong" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                  {money(i.total, currency)}
                </Box>
              </Box>
            ))}
          </Box>
        )}
      </Paper>
    </Grid>
  );
}

function GoalLine({ status, money }: { status: MonthlyGoalScopeStatus; money: (v: number) => string }) {
  const situation = GOAL_SITUATION[status.situation!];
  return (
    <Box
      component="li"
      data-cy={`summary-goal-${status.scope}`}
      data-situation={status.situation}
      sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1.5 }}
    >
      <span>{status.scope === 'business' ? 'Empresa (PJ)' : 'Pessoal (PF)'}</span>
      <StatusChip tone={situation.tone} label={situation.label} />
      <span>ainda pode gastar {money(status.canStillSpend ?? 0)}</span>
    </Box>
  );
}
