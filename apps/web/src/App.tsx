import { useEffect, useState } from 'react';
import {
  beginLogin,
  completeLogin,
  getUserInfo,
  isAuthenticated,
  logout,
  type UserInfo,
} from './auth/oauth';
import { AccountsView } from './views/AccountsView';
import { CardsView } from './views/CardsView';
import { TransactionsView } from './views/TransactionsView';

type Status = 'loading' | 'anonymous' | 'authenticated' | 'error';
type Tab = 'accounts' | 'cards' | 'transactions';

export function App() {
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<UserInfo | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('transactions');

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
          <button className="primary" onClick={() => void beginLogin()}>
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
          <button className={tab === 'transactions' ? 'active' : ''} onClick={() => setTab('transactions')}>
            Lançamentos
          </button>
          <button className={tab === 'accounts' ? 'active' : ''} onClick={() => setTab('accounts')}>
            Contas
          </button>
          <button className={tab === 'cards' ? 'active' : ''} onClick={() => setTab('cards')}>
            Cartões
          </button>
        </nav>
        <div className="user">
          <span title={user?.sub ?? ''}>{user?.scope?.includes('expenses.write') ? '✎ ' : ''}sessão ativa</span>
          <button className="ghost" onClick={() => logout()}>
            Sair
          </button>
        </div>
      </header>

      <main className="content">
        {tab === 'transactions' && <TransactionsView />}
        {tab === 'accounts' && <AccountsView />}
        {tab === 'cards' && <CardsView />}
      </main>
    </div>
  );
}
