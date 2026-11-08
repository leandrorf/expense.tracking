import { useEffect, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import type { Account } from '../api/types';

export function AccountsView() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [scope, setScope] = useState<'Personal' | 'Business'>('Personal');
  const [taxId, setTaxId] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setAccounts(await accountsApi.list());
    } catch (err) {
      setError(messageOf(err));
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
    setError(null);
    try {
      await accountsApi.create({
        name,
        scope,
        taxId: taxId.trim() ? taxId.trim() : undefined,
      });
      setName('');
      setTaxId('');
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemove(id: string) {
    try {
      await accountsApi.remove(id);
      await load();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  return (
    <section>
      <h2>Contas</h2>

      <form className="row-form" onSubmit={handleCreate} data-cy="account-form">
        <input
          data-cy="account-name"
          placeholder="Nome da conta"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
        <select data-cy="account-scope" value={scope} onChange={(e) => setScope(e.target.value as 'Personal' | 'Business')}>
          <option value="Personal">Pessoal (PF)</option>
          <option value="Business">Empresa (PJ)</option>
        </select>
        <input
          data-cy="account-taxid"
          placeholder={scope === 'Business' ? 'CNPJ (opcional)' : 'CPF (opcional)'}
          value={taxId}
          onChange={(e) => setTaxId(e.target.value)}
        />
        <button data-cy="account-submit" className="primary" type="submit" disabled={submitting}>
          {submitting ? 'Criando…' : 'Adicionar'}
        </button>
      </form>

      {error && <p className="error">{error}</p>}
      {loading ? (
        <p>Carregando…</p>
      ) : accounts.length === 0 ? (
        <p className="muted">Nenhuma conta ainda.</p>
      ) : (
        <table className="data" data-cy="accounts-table">
          <thead>
            <tr>
              <th>Nome</th>
              <th>Escopo</th>
              <th>Documento</th>
              <th>Moeda</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {accounts.map((a) => (
              <tr key={a.id} className={a.isActive ? '' : 'inactive'}>
                <td>{a.name}</td>
                <td>{a.scope === 'Business' ? 'PJ' : 'PF'}</td>
                <td>{a.taxId ?? '—'}</td>
                <td>{a.currency}</td>
                <td>
                  {a.isActive && (
                    <button className="link-danger" onClick={() => void handleRemove(a.id)}>
                      desativar
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
