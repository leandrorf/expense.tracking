import { useEffect, useState, type ReactNode } from 'react';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableFooter from '@mui/material/TableFooter';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import { ApiError } from '../api/client';
import { goalsApi, type MonthlyGoal } from '../api/goals';
import { forecastApi, type CashForecast, type MonthFlow } from '../api/reports';
import { goalOfMonth, termFor } from '../goalMath';
import { Notice, SelectField } from '../ui';

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
  // MET-8: metas mensais, para a coluna "Meta" de PF e PJ.
  const [goals, setGoals] = useState<MonthlyGoal[]>([]);

  async function load(horizon: number) {
    setLoading(true);
    setError(null);
    try {
      const [loaded, monthly] = await Promise.all([forecastApi.get(horizon), goalsApi.listMonthly().catch(() => [])]);
      setForecast(loaded);
      setGoals(monthly);
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
  // MET-8: a meta de cada mês e a sobra depois dela, so em PF ou PJ e se algum mes tem meta.
  const goalRows = scope === 'total' ? [] : rows.map((r) => termFor(goals, scope, r.month));
  const showGoal = goalRows.some((t) => t !== null);

  return (
    <section data-cy="forecast">
      <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
        Previsão de caixa
      </Typography>

      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mb: 2 }}>
        <SelectField
          id="forecast-scope"
          label="Escopo"
          dataCy="forecast-scope"
          value={scope}
          onChange={(v) => setScope(v as Scope)}
          options={SCOPES}
          sx={{ minWidth: 160 }}
        />
        <SelectField
          id="forecast-months"
          label="Meses"
          dataCy="forecast-months"
          value={String(months)}
          onChange={(v) => setMonths(Number(v))}
          options={HORIZONS.map((h) => ({ value: String(h), label: h }))}
          sx={{ minWidth: 90 }}
        />
        <Button variant="outlined" color="inherit" data-cy="forecast-refresh" onClick={() => void load(months)}>
          atualizar
        </Button>
      </Stack>

      {forecast && (
        <Notice dataCy="forecast-notice">
          {forecast.notice} Compras no cartão entram pela fatura, no mês do vencimento.{' '}
          <span data-cy="forecast-recurring-notice">
            Recorrências entram como "previsto" de hoje em diante, até você lançar ou pular cada ocorrência (aba
            Recorrências); se você importar o extrato, pule a ocorrência que veio por ele para não contar duas vezes. O
            reajuste anual ainda não confirmado entra estimado, pelo último acumulado do índice ou pelo percentual fixo.
          </span>
        </Notice>
      )}
      {forecast && forecast.accountsWithoutBalance > 0 && (
        <Notice severity="warning" dataCy="forecast-balance-notice">
          {forecast.accountsWithoutBalance === 1
            ? '1 conta ativa está sem saldo informado e fica fora do saldo projetado.'
            : `${forecast.accountsWithoutBalance} contas ativas estão sem saldo informado e ficam fora do saldo projetado.`}{' '}
          Informe o saldo na aba Contas.
        </Notice>
      )}

      {error && <Notice severity="error">{error}</Notice>}
      {loading && !forecast ? (
        <Typography>Carregando…</Typography>
      ) : (
        // A tabela rola dentro do próprio contêiner no celular (MUI-3).
        <TableContainer component={Paper} variant="outlined">
          <Table data-cy="forecast-table" sx={{ minWidth: 720 }}>
            <TableHead>
              <TableRow>
                <TableCell>Mês</TableCell>
                <TableCell align="right">Entradas</TableCell>
                <TableCell align="right">Saídas</TableCell>
                <TableCell align="right">Faturas</TableCell>
                <TableCell align="right">Resultado</TableCell>
                {showGoal && (
                  <TableCell align="right" title="Meta mensal do escopo e quanto sobra do resultado depois dela (aba Metas)">
                    Meta
                  </TableCell>
                )}
                <TableCell align="right" title="Saldo das contas com saldo informado, no fim do mês">
                  Saldo no fim do mês
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map(({ month, flow }) => (
                <TableRow key={month} data-cy="forecast-row" data-month={month} sx={{ verticalAlign: 'top' }}>
                  <TableCell>{monthLabel(month)}</TableCell>
                  <TableCell align="right" data-cy="forecast-income">
                    {money(flow.income)}
                    {flow.recurringIncome > 0 && (
                      <Sub recurring dataCy="forecast-income-recurring">
                        {money(flow.recurringIncome)} previsto
                      </Sub>
                    )}
                  </TableCell>
                  <TableCell align="right" data-cy="forecast-expense">
                    {money(flow.expense)}
                    {flow.recurringExpense > 0 && (
                      <Sub recurring dataCy="forecast-expense-recurring">
                        {money(flow.recurringExpense)} previsto
                      </Sub>
                    )}
                  </TableCell>
                  <TableCell align="right" data-cy="forecast-invoices">
                    {money(flow.invoices)}
                    {flow.invoices > 0 && (
                      <Sub dataCy="forecast-invoices-remaining">
                        {flow.invoicesRemaining > 0 ? `falta pagar ${money(flow.invoicesRemaining)}` : 'paga'}
                      </Sub>
                    )}
                    {flow.recurringInvoices !== 0 && (
                      <Sub recurring dataCy="forecast-invoices-recurring">
                        {money(flow.recurringInvoices)} previsto
                      </Sub>
                    )}
                  </TableCell>
                  <TableCell align="right" data-cy="forecast-result" sx={{ color: tone(flow.result, true), fontWeight: 600 }}>
                    {money(flow.result)}
                    {flow.estimatedRecurring > 0 && (
                      <Sub recurring dataCy="forecast-estimated" title="Reajuste anual ainda não confirmado na aba Recorrências">
                        inclui {money(flow.estimatedRecurring)} de reajuste estimado
                      </Sub>
                    )}
                  </TableCell>
                  {showGoal && <GoalCell term={goalRows[rows.findIndex((r) => r.month === month)]} flow={flow} money={money} />}
                  <TableCell align="right" data-cy="forecast-balance" sx={{ color: tone(flow.balance) }}>
                    {flow.balance === null ? (
                      <Box component="span" sx={{ color: 'text.secondary' }}>
                        sem saldo
                      </Box>
                    ) : (
                      money(flow.balance)
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
            <TableFooter>
              <TableRow data-cy="forecast-sum" sx={{ '& th': { fontWeight: 700, color: 'text.primary', fontSize: '0.875rem' } }}>
                <TableCell component="th">Período</TableCell>
                <TableCell component="th" align="right">{money(sum((f) => f.income))}</TableCell>
                <TableCell component="th" align="right">{money(sum((f) => f.expense))}</TableCell>
                <TableCell component="th" align="right">{money(sum((f) => f.invoices))}</TableCell>
                <TableCell component="th" align="right">{money(sum((f) => f.result))}</TableCell>
                {showGoal && <TableCell component="th" />}
                <TableCell component="th" />
              </TableRow>
            </TableFooter>
          </Table>
        </TableContainer>
      )}
    </section>
  );
}

/** MET-8: a meta do mes (ou "sem meta") e a sobra depois dela. */
function GoalCell({
  term,
  flow,
  money,
}: {
  term: ReturnType<typeof termFor>;
  flow: MonthFlow;
  money: (value: number) => string;
}) {
  if (!term) {
    return (
      <TableCell align="right" data-cy="forecast-goal" sx={{ color: 'text.secondary' }}>
        sem meta
      </TableCell>
    );
  }

  const { target, left } = goalOfMonth(term, flow);
  const label = term.kind === 'SpendCeiling' ? `teto ${money(target)}` : `guardar ${money(target)}`;
  return (
    <TableCell align="right" data-cy="forecast-goal" sx={{ color: left < 0 ? 'error.main' : undefined }}>
      {label}
      <Sub dataCy="forecast-goal-left">{left < 0 ? `faltam ${money(-left)}` : `sobra ${money(left)}`}</Sub>
    </TableCell>
  );
}

/** Linha menor dentro da célula: "previsto" (recorrência, em itálico), "falta pagar", a sobra da meta. */
function Sub({ children, dataCy, recurring, title }: { children: ReactNode; dataCy?: string; recurring?: boolean; title?: string }) {
  return (
    <Box
      component="span"
      data-cy={dataCy}
      title={title}
      sx={{
        display: 'block',
        fontSize: '0.75rem',
        color: recurring ? '#a78bfa' : 'text.secondary',
        fontStyle: recurring ? 'italic' : undefined,
      }}
    >
      {children}
    </Box>
  );
}

/** Cor do valor: negativo em vermelho, positivo em verde (resultado). */
function tone(value: number | null, positive = false) {
  if (value === null) return undefined;
  if (value < 0) return 'error.main';
  return positive && value > 0 ? 'success.main' : undefined;
}
