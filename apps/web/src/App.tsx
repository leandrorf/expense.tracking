import { useEffect, useState } from 'react';
import {
  beginLogin,
  completeLogin,
  getUserInfo,
  isAuthenticated,
  logout,
  refresh,
  type UserInfo,
} from './auth/oauth';

type Status = 'loading' | 'anonymous' | 'authenticated' | 'error';

export function App() {
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<UserInfo | null>(null);
  const [message, setMessage] = useState<string | null>(null);

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

  async function handleRefresh() {
    setMessage(null);
    const ok = await refresh();
    if (ok) {
      setUser(await getUserInfo());
      setMessage('Token renovado com sucesso.');
    } else {
      setMessage('Nao foi possivel renovar o token.');
      setStatus('anonymous');
    }
  }

  return (
    <main className="shell">
      <div className="card">
        <h1>Expense Tracking</h1>
        <p className="subtitle">Controle de gastos pessoal e empresarial</p>

        {status === 'loading' && <p>Carregando…</p>}

        {status === 'anonymous' && (
          <>
            <p>Voce ainda nao esta autenticado.</p>
            <button className="primary" onClick={() => void beginLogin()}>
              Entrar com OAuth2 (PKCE)
            </button>
          </>
        )}

        {status === 'authenticated' && user && (
          <>
            <p className="ok">Autenticado ✓</p>
            <dl className="info">
              <dt>sub</dt>
              <dd>{user.sub}</dd>
              <dt>client_id</dt>
              <dd>{user.client_id}</dd>
              <dt>scope</dt>
              <dd>{user.scope}</dd>
            </dl>
            <div className="actions">
              <button onClick={() => void handleRefresh()}>Renovar token</button>
              <button className="ghost" onClick={() => logout()}>
                Sair
              </button>
            </div>
          </>
        )}

        {status === 'error' && (
          <>
            <p className="error">{message}</p>
            <button className="primary" onClick={() => void beginLogin()}>
              Tentar novamente
            </button>
          </>
        )}

        {message && status === 'authenticated' && <p className="hint">{message}</p>}
      </div>
    </main>
  );
}
