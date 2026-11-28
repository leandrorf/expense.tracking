import { useEffect, useState, type ReactNode } from 'react';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { Notice } from './ui';
import {
  beginLogin,
  completeLogin,
  getUserInfo,
  isAuthenticated,
  logout,
  type UserInfo,
} from './auth/oauth';
import { AccountingExportView } from './views/AccountingExportView';
import { AccountsView } from './views/AccountsView';
import { BudgetsView } from './views/BudgetsView';
import { CardsView } from './views/CardsView';
import { ForecastView } from './views/ForecastView';
import { GoalsView } from './views/GoalsView';
import { ImportView } from './views/ImportView';
import { RecurrencesView } from './views/RecurrencesView';
import { RemindersBell } from './views/RemindersBell';
import { remindersApi, type Reminder } from './api/reminders';

/** NTF-8: token do link de confirmação do e-mail, guardado até o login terminar. */
const PENDING_EMAIL_TOKEN = 'expense.pendingEmailToken';
import type { Transaction } from './api/types';
import { SummaryView } from './views/SummaryView';
import { TransactionsView } from './views/TransactionsView';

type Status = 'loading' | 'anonymous' | 'authenticated' | 'error';
type Tab = 'summary' | 'forecast' | 'budgets' | 'goals' | 'accounts' | 'cards' | 'transactions' | 'recurrences' | 'import' | 'accounting';

/** Abas na ordem da barra; o `data-cy` de cada uma é `tab-<valor>`. */
const TABS: { value: Tab; label: string }[] = [
  { value: 'summary', label: 'Resumo' },
  { value: 'forecast', label: 'Previsão' },
  { value: 'transactions', label: 'Lançamentos' },
  { value: 'recurrences', label: 'Recorrências' },
  { value: 'import', label: 'Importar' },
  { value: 'budgets', label: 'Orçamentos' },
  { value: 'goals', label: 'Metas' },
  { value: 'accounts', label: 'Contas' },
  { value: 'cards', label: 'Cartões' },
  { value: 'accounting', label: 'Contador' },
];

/** Tela central (carregando, login): cartão no meio da página. */
function Centered({ children }: { children: ReactNode }) {
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <Paper elevation={8} sx={{ width: '100%', maxWidth: 420, p: 4, borderRadius: 4 }}>
        {children}
      </Paper>
    </Box>
  );
}

export function App() {
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<UserInfo | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('summary');
  // REC-10: lançamento escolhido em "repetir todo mês", que abre a aba Recorrências preenchida.
  const [repeatFrom, setRepeatFrom] = useState<Transaction | null>(null);
  // NTF-3: fatura que um aviso pediu para abrir na aba Cartões.
  const [invoiceTarget, setInvoiceTarget] = useState<{ cardId: string; dueMonth: string } | null>(null);
  // RCN-6: lançamento que a conferência da fatura mandou editar.
  const [editTarget, setEditTarget] = useState<string | null>(null);
  // NTF-8: resultado da confirmação do e-mail dos avisos (link do e-mail).
  const [emailConfirmation, setEmailConfirmation] = useState<{ ok: boolean; text: string } | null>(null);

  // RJT-9: regra cujo reajuste pendente o aviso abriu.
  const [adjustTarget, setAdjustTarget] = useState<string | null>(null);

  /** NTF-3 (ADR-0009, decisão 7): cada aviso leva aonde se resolve. */
  function openReminder(r: Reminder) {
    if ((r.kind === 'InvoiceDue' || r.kind === 'InvoiceOverdue') && r.cardId && r.dueMonth) {
      setInvoiceTarget({ cardId: r.cardId, dueMonth: r.dueMonth });
      setTab('cards');
    } else if (r.kind === 'Budget') {
      setTab('budgets');
    } else if (r.kind === 'MonthlyGoal' || r.kind === 'SavingsObjective') {
      // MET-9: meta do mês fora ou objetivo atrasado abrem a aba Metas.
      setTab('goals');
    } else if (r.kind === 'RecurrenceAdjustment' && r.recurrenceId) {
      // RJT-9: abre os valores da regra, onde o reajuste e confirmado.
      setAdjustTarget(r.recurrenceId);
      setTab('recurrences');
    } else {
      setTab('recurrences');
    }
  }

  useEffect(() => {
    void bootstrap();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function bootstrap() {
    try {
      // NTF-8: o link do e-mail abre /confirm-email?token=...; o token espera o login, se preciso.
      if (window.location.pathname === '/confirm-email') {
        const token = new URLSearchParams(window.location.search).get('token');
        if (token) sessionStorage.setItem(PENDING_EMAIL_TOKEN, token);
        window.history.replaceState({}, '', '/');
      }

      if (window.location.pathname === '/callback') {
        await completeLogin();
        window.history.replaceState({}, '', '/');
      }

      if (isAuthenticated()) {
        setUser(await getUserInfo());
        setStatus('authenticated');
        void confirmPendingEmail();
      } else {
        setStatus('anonymous');
      }
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Erro inesperado.');
      setStatus('error');
    }
  }

  async function confirmPendingEmail() {
    const token = sessionStorage.getItem(PENDING_EMAIL_TOKEN);
    if (!token) return;
    sessionStorage.removeItem(PENDING_EMAIL_TOKEN);
    try {
      const confirmed = await remindersApi.confirmEmail(token);
      setEmailConfirmation({ ok: true, text: `E-mail ${confirmed.email} confirmado: o resumo diário dos avisos vai para ele.` });
    } catch (err) {
      setEmailConfirmation({ ok: false, text: err instanceof Error ? err.message : 'Não foi possível confirmar o e-mail.' });
    }
  }

  if (status === 'loading') {
    return (
      <Centered>
        <Typography>Carregando…</Typography>
      </Centered>
    );
  }

  if (status !== 'authenticated') {
    return (
      <Centered>
        <Typography variant="h1" component="h1">
          Expense Tracking
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 3 }}>
          Controle de gastos pessoal e empresarial
        </Typography>
        {message && (
          <Notice severity="error" dataCy="login-error">
            {message}
          </Notice>
        )}
        <Button data-cy="login" variant="contained" size="large" fullWidth onClick={() => void beginLogin()}>
          Entrar com OAuth2 (PKCE)
        </Button>
      </Centered>
    );
  }

  return (
    <Box sx={{ minHeight: '100vh' }}>
      <AppBar position="sticky" color="inherit" elevation={0} sx={{ bgcolor: 'background.paper', borderBottom: 1, borderColor: 'divider' }}>
        <Toolbar variant="dense" sx={{ gap: 2, flexWrap: { xs: 'wrap', md: 'nowrap' }, py: { xs: 0.5, md: 0 } }}>
          <Typography component="strong" sx={{ fontWeight: 700, whiteSpace: 'nowrap', order: 0 }}>
            Expense Tracking
          </Typography>
          <Tabs
            component="nav"
            value={tab}
            onChange={(_, value: Tab) => setTab(value)}
            variant="scrollable"
            scrollButtons="auto"
            allowScrollButtonsMobile
            aria-label="Telas"
            sx={{ flex: { xs: '1 1 100%', md: '1 1 0%' }, minWidth: 0, order: { xs: 2, md: 1 } }}
          >
            {TABS.map((t) => (
              <Tab key={t.value} value={t.value} label={t.label} data-cy={`tab-${t.value}`} />
            ))}
          </Tabs>
          <Stack
            direction="row"
            spacing={1.5}
            sx={{ alignItems: 'center', ml: 'auto', order: { xs: 1, md: 2 }, fontSize: '0.8rem', color: 'text.secondary', whiteSpace: 'nowrap' }}
          >
            <RemindersBell refreshKey={tab} onOpen={openReminder} />
            <Box component="span" data-cy="session-active" title={user?.sub ?? ''} sx={{ display: { xs: 'none', sm: 'inline' } }}>
              {user?.scope?.includes('expenses.write') ? '✎ ' : ''}sessão ativa
            </Box>
            <Button data-cy="logout" variant="outlined" color="inherit" onClick={() => logout()}>
              Sair
            </Button>
          </Stack>
        </Toolbar>
      </AppBar>

      <main className="content">
        {emailConfirmation && (
          <Notice
            severity={emailConfirmation.ok ? 'success' : 'warning'}
            dataCy="email-confirmation"
            data-ok={emailConfirmation.ok}
            onClose={() => setEmailConfirmation(null)}
            slotProps={{ closeButton: { title: 'fechar' } }}
          >
            {emailConfirmation.text}
          </Notice>
        )}
        {tab === 'summary' && <SummaryView onOpenGoals={() => setTab('goals')} />}
        {tab === 'forecast' && <ForecastView />}
        {tab === 'transactions' && (
          <TransactionsView
            onRepeat={(t) => {
              setRepeatFrom(t);
              setTab('recurrences');
            }}
            editId={editTarget}
            onEditOpened={() => setEditTarget(null)}
          />
        )}
        {tab === 'recurrences' && (
          <RecurrencesView
            template={repeatFrom}
            onTemplateUsed={() => setRepeatFrom(null)}
            focusValuesOf={adjustTarget}
            onFocusUsed={() => setAdjustTarget(null)}
          />
        )}
        {tab === 'import' && <ImportView onShowTransactions={() => setTab('transactions')} />}
        {tab === 'budgets' && <BudgetsView />}
        {tab === 'goals' && <GoalsView />}
        {tab === 'accounts' && <AccountsView />}
        {tab === 'cards' && (
          <CardsView
            openInvoice={invoiceTarget}
            onInvoiceOpened={() => setInvoiceTarget(null)}
            onEditTransaction={(id) => {
              setEditTarget(id);
              setTab('transactions');
            }}
          />
        )}
        {tab === 'accounting' && <AccountingExportView />}
      </main>
    </Box>
  );
}
