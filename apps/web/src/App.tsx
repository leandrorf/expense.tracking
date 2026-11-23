import { useEffect, useState } from 'react';
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
import { ImportView } from './views/ImportView';
import { RecurrencesView } from './views/RecurrencesView';
import { RemindersBell } from './views/RemindersBell';
import type { Reminder } from './api/reminders';
import type { Transaction } from './api/types';
import { SummaryView } from './views/SummaryView';
import { TransactionsView } from './views/TransactionsView';

type Status = 'loading' | 'anonymous' | 'authenticated' | 'error';
type Tab = 'summary' | 'forecast' | 'budgets' | 'accounts' | 'cards' | 'transactions' | 'recurrences' | 'import' | 'accounting';

export function App() {
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<UserInfo | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('summary');
  // REC-10: lançamento escolhido em "repetir todo mês", que abre a aba Recorrências preenchida.
  const [repeatFrom, setRepeatFrom] = useState<Transaction | null>(null);
  // NTF-3: fatura que um aviso pediu para abrir na aba Cartões.
  const [invoiceTarget, setInvoiceTarget] = useState<{ cardId: string; dueMonth: string } | null>(null);

  /** NTF-3 (ADR-0009, decisão 7): cada aviso leva aonde se resolve. */
  function openReminder(r: Reminder) {
    if ((r.kind === 'InvoiceDue' || r.kind === 'InvoiceOverdue') && r.cardId && r.dueMonth) {
      setInvoiceTarget({ cardId: r.cardId, dueMonth: r.dueMonth });
      setTab('cards');
    } else if (r.kind === 'Budget') {
      setTab('budgets');
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
      if (window.location.pathname === '/callback') {
        await completeLogin();
        window.history.replaceState({}, '', '/');
      }

      if (isAuthenticated()) {
        setUser(await getUserInfo());
        setStatus('authenticated');
      } else {
        setStatus('anonymous');
      }
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Erro inesperado.');
      setStatus('error');
    }
  }

  if (status === 'loading') {
    return (
      <main className="shell">
        <div className="card">
          <p>Carregando…</p>
        </div>
      </main>
    );
  }

  if (status !== 'authenticated') {
    return (
      <main className="shell">
        <div className="card">
          <h1>Expense Tracking</h1>
          <p className="subtitle">Controle de gastos pessoal e empresarial</p>
          {message && <p className="error">{message}</p>}
          <button data-cy="login" className="primary" onClick={() => void beginLogin()}>
            Entrar com OAuth2 (PKCE)
          </button>
        </div>
      </main>
    );
  }

  return (
    <div className="app">
      <header className="topbar">
        <strong>Expense Tracking</strong>
        <nav className="tabs">
          <button data-cy="tab-summary" className={tab === 'summary' ? 'active' : ''} onClick={() => setTab('summary')}>
            Resumo
          </button>
          <button data-cy="tab-forecast" className={tab === 'forecast' ? 'active' : ''} onClick={() => setTab('forecast')}>
            Previsão
          </button>
          <button data-cy="tab-transactions" className={tab === 'transactions' ? 'active' : ''} onClick={() => setTab('transactions')}>
            Lançamentos
          </button>
          <button data-cy="tab-recurrences" className={tab === 'recurrences' ? 'active' : ''} onClick={() => setTab('recurrences')}>
            Recorrências
          </button>
          <button data-cy="tab-import" className={tab === 'import' ? 'active' : ''} onClick={() => setTab('import')}>
            Importar
          </button>
          <button data-cy="tab-budgets" className={tab === 'budgets' ? 'active' : ''} onClick={() => setTab('budgets')}>
            Orçamentos
          </button>
          <button data-cy="tab-accounts" className={tab === 'accounts' ? 'active' : ''} onClick={() => setTab('accounts')}>
            Contas
          </button>
          <button data-cy="tab-cards" className={tab === 'cards' ? 'active' : ''} onClick={() => setTab('cards')}>
            Cartões
          </button>
          <button data-cy="tab-accounting" className={tab === 'accounting' ? 'active' : ''} onClick={() => setTab('accounting')}>
            Contador
          </button>
        </nav>
        <div className="user">
          <RemindersBell refreshKey={tab} onOpen={openReminder} />
          <span data-cy="session-active" title={user?.sub ?? ''}>{user?.scope?.includes('expenses.write') ? '✎ ' : ''}sessão ativa</span>
          <button data-cy="logout" className="ghost" onClick={() => logout()}>
            Sair
          </button>
        </div>
      </header>

      <main className="content">
        {tab === 'summary' && <SummaryView />}
        {tab === 'forecast' && <ForecastView />}
        {tab === 'transactions' && (
          <TransactionsView
            onRepeat={(t) => {
              setRepeatFrom(t);
              setTab('recurrences');
            }}
          />
        )}
        {tab === 'recurrences' && <RecurrencesView template={repeatFrom} onTemplateUsed={() => setRepeatFrom(null)} />}
        {tab === 'import' && <ImportView onShowTransactions={() => setTab('transactions')} />}
        {tab === 'budgets' && <BudgetsView />}
        {tab === 'accounts' && <AccountsView />}
        {tab === 'cards' && <CardsView openInvoice={invoiceTarget} onInvoiceOpened={() => setInvoiceTarget(null)} />}
        {tab === 'accounting' && <AccountingExportView />}
      </main>
    </div>
  );
}
