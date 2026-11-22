import { useEffect, useState } from 'react';
import { ledgerAccountsApi, type LedgerAccount } from '../api/accountingExport';
import { ApiError } from '../api/client';

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface RowProps {
  ledger: LedgerAccount;
  onChanged: () => void;
}

function LedgerRow({ ledger, onChanged }: RowProps) {
  const [code, setCode] = useState(ledger.code ?? '');
  const [name, setName] = useState(ledger.name ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dirty = code.trim() !== (ledger.code ?? '') || name.trim() !== (ledger.name ?? '');

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChanged();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <tr data-cy="ledger-row" data-category={ledger.category}>
      <td>{ledger.category}</td>
      <td className="right">{ledger.lines}</td>
      <td>
        <input
          data-cy="ledger-code"
          aria-label={`Código da conta contábil de ${ledger.category}`}
          placeholder="ex.: 3.1.02"
          maxLength={20}
          value={code}
          onChange={(e) => setCode(e.target.value)}
        />
      </td>
      <td>
        <input
          data-cy="ledger-name"
          aria-label={`Nome da conta contábil de ${ledger.category}`}
          placeholder="opcional"
          maxLength={100}
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        {error && <span className="error sub">{error}</span>}
      </td>
      <td>
        <button
          type="button"
          className="primary small"
          data-cy="ledger-save"
          disabled={busy || !dirty || !code.trim()}
          onClick={() => void run(() => ledgerAccountsApi.set(ledger.category, code.trim(), name.trim()))}
        >
          Salvar
        </button>
        {ledger.code && (
          <button
            type="button"
            className="ghost small"
            data-cy="ledger-remove"
            disabled={busy}
            onClick={() => void run(() => ledgerAccountsApi.remove(ledger.category))}
          >
            Remover
          </button>
        )}
      </td>
    </tr>
  );
}

/**
 * CTB-5 (ADR-0006, decisão 7): plano de contas. Cada categoria das contas PJ pode ganhar o código
 * da conta no plano do contador, que sai na coluna "Conta contábil" da exportação. Sem código, a
 * coluna sai vazia e a exportação continua.
 */
export function LedgerAccountsSection({ onChanged }: { onChanged: () => void }) {
  const [ledgers, setLedgers] = useState<LedgerAccount[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      setLedgers(await ledgerAccountsApi.list());
    } catch (err) {
      setError(messageOf(err));
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function changed() {
    void load();
    onChanged();
  }

  return (
    <section data-cy="ledger-accounts">
      <h3>Plano de contas</h3>
      <p className="muted">
        Código da conta no plano do seu contador para cada categoria PJ. Ele sai na coluna “Conta contábil” das
        planilhas; a categoria vale como foi digitada.
      </p>
      {error && <p className="error">{error}</p>}
      {ledgers === null ? (
        !error && <p>Carregando…</p>
      ) : ledgers.length === 0 ? (
        <p className="muted" data-cy="ledger-empty">Nenhuma categoria lançada em conta PJ ainda.</p>
      ) : (
        <div className="table-scroll">
          <table className="data" data-cy="ledger-table">
            <thead>
              <tr>
                <th>Categoria</th>
                <th className="right">Lançamentos</th>
                <th>Código</th>
                <th>Nome</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {ledgers.map((l) => (
                <LedgerRow key={`${l.category}|${l.code ?? ''}|${l.name ?? ''}`} ledger={l} onChanged={changed} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
