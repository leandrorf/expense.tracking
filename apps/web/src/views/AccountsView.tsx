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
  // INV-13: uma conta por vez tem o saldo em edicao, na propria linha.
  const [balanceFor, setBalanceFor] = useState<string | null>(null);
  const [balanceAmount, setBalanceAmount] = useState('');
  const [balanceDate, setBalanceDate] = useState(today());

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

  function startBalance(a: Account) {
    setBalanceFor(a.id);
    setBalanceAmount(a.openingBalance === null ? '' : String(a.openingBalance));
    setBalanceDate(a.openingBalanceOn ?? today());
    setError(null);
  }

  async function handleSaveBalance(e: React.FormEvent) {
    e.preventDefault();
    if (!balanceFor) return;
    setError(null);
    try {
      await accountsApi.setOpeningBalance(balanceFor, Number(balanceAmount), balanceDate);
      setBalanceFor(null);
      await load();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  async function handleClearBalance(id: string) {
    setError(null);
    try {
      await accountsApi.clearOpeningBalance(id);
      setBalanceFor(null);
      await load();
    } catch (err) {
      setError(messageOf(err));
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
              <th title="Ponto de partida do saldo projetado na Previsão">Saldo</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {accounts.map((a) => (
              <tr key={a.id} className={a.isActive ? '' : 'inactive'} data-cy="account-row">
                <td>{a.name}</td>
                <td>{a.scope === 'Business' ? 'PJ' : 'PF'}</td>
                <td>{a.taxId ?? '—'}</td>
                <td>{a.currency}</td>
                <td data-cy="account-balance">
                  {balanceFor === a.id ? (
                    <form className="row-form inline" onSubmit={handleSaveBalance} data-cy="account-balance-form">
                      <input
                        data-cy="account-balance-amount"
                        aria-label="Saldo"
                        placeholder="Saldo"
                        type="number"
                        step="0.01"
                        value={balanceAmount}
                        onChange={(e) => setBalanceAmount(e.target.value)}
                        required
                      />
                      <input
                        data-cy="account-balance-date"
                        aria-label="No fim do dia"
                        title="Saldo no fim deste dia; o que vier depois entra na projeção"
                        type="date"
                        max={today()}
                        value={balanceDate}
                        onChange={(e) => setBalanceDate(e.target.value)}
                        required
                      />
                      <button data-cy="account-balance-save" className="primary small" type="submit">
                        Salvar
                      </button>
                      <button className="ghost small" type="button" onClick={() => setBalanceFor(null)}>
                        Cancelar
                      </button>
                    </form>
                  ) : (
                    <>
                      {a.openingBalance === null ? (
                        <span className="muted">—</span>
                      ) : (
                        <span data-cy="account-balance-value">
                          {a.openingBalance.toLocaleString('pt-BR', { style: 'currency', currency: a.currency })}
                          <span className="sub">em {dateLabel(a.openingBalanceOn!)}</span>
                        </span>
                      )}{' '}
                      <button className="link" data-cy="account-balance-edit" onClick={() => startBalance(a)}>
                        {a.openingBalance === null ? 'informar' : 'alterar'}
                      </button>
                      {a.openingBalance !== null && (
                        <button className="link-danger" data-cy="account-balance-clear" onClick={() => void handleClearBalance(a.id)}>
                          remover
                        </button>
                      )}
                    </>
                  )}
                </td>
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

/** Hoje no fuso do navegador, `yyyy-MM-dd`. */
function today(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** `2026-11-10` → `10/11/2026`. */
function dateLabel(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
