import { useEffect, useState } from 'react';
import { accountingExportApi, type AccountingExportPreview } from '../api/accountingExport';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import type { Account } from '../api/types';
import { LedgerAccountsSection } from './LedgerAccountsSection';

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/** Mês anterior ao de hoje, `yyyy-MM`: é o que se manda ao contador. */
export function previousMonth(today = new Date()): string {
  const d = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

/**
 * CTB-4 (ADR-0006): exportação para o contador. Mostra quantos lançamentos e pagamentos de fatura
 * entram e os totais (os mesmos do resumo.csv) antes de baixar o ZIP do mês. Só contas PJ.
 */
export function AccountingExportView() {
  const [accounts, setAccounts] = useState<Account[] | null>(null);
  const [month, setMonth] = useState(previousMonth());
  const [accountId, setAccountId] = useState('');
  const [preview, setPreview] = useState<AccountingExportPreview | null>(null);
  const [loading, setLoading] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Muda quando o plano de contas muda (CTB-5) ou depois de baixar (CTB-6), para recarregar a prévia.
  const [refreshKey, setRefreshKey] = useState(0);

  const business = (accounts ?? []).filter((a) => a.scope === 'Business');

  useEffect(() => {
    accountsApi.list().then(setAccounts, (err) => setError(messageOf(err)));
  }, []);

  useEffect(() => {
    if (business.length === 0 || !month) return;
    let current = true;
    setLoading(true);
    setError(null);
    accountingExportApi
      .preview(month, accountId || undefined)
      .then(
        (p) => current && setPreview(p),
        (err) => {
          if (!current) return;
          setPreview(null);
          setError(messageOf(err));
        },
      )
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accounts, month, accountId, refreshKey]);

  async function download() {
    if (!preview) return;
    setDownloading(true);
    setError(null);
    try {
      const blob = await accountingExportApi.download(month, accountId || undefined);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = preview.fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      setRefreshKey((k) => k + 1);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setDownloading(false);
    }
  }

  const money = (value: number, currency: string) => value.toLocaleString('pt-BR', { style: 'currency', currency });
  const when = (utc: string) =>
    new Date(utc).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });

  if (accounts === null) {
    return (
      <section data-cy="accounting-export">
        <h2>Exportar para o contador</h2>
        {error ? <p className="error">{error}</p> : <p>Carregando…</p>}
      </section>
    );
  }

  return (
    <section data-cy="accounting-export">
      <h2>Exportar para o contador</h2>

      {business.length === 0 ? (
        <p className="notice" data-cy="accounting-no-business">
          A exportação contábil é só para contas PJ. Cadastre uma conta empresarial (com CNPJ) em Contas para usá-la.
        </p>
      ) : (
        <>
          <p className="notice">
            Um ZIP com três planilhas que abrem no Excel: lançamentos, pagamentos de fatura e resumo por categoria. Só
            entram contas PJ; os lançamentos contam pela data, e a compra parcelada entra inteira.
          </p>

          <div className="filters">
            <label htmlFor="accounting-month">Mês:</label>
            <input
              id="accounting-month"
              data-cy="accounting-month"
              type="month"
              value={month}
              onChange={(e) => setMonth(e.target.value)}
            />
            {business.length > 1 && (
              <>
                <label htmlFor="accounting-account">Conta:</label>
                <select
                  id="accounting-account"
                  data-cy="accounting-account"
                  value={accountId}
                  onChange={(e) => setAccountId(e.target.value)}
                >
                  <option value="">Todas as contas PJ</option>
                  {business.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.name}
                      {a.isActive ? '' : ' (inativa)'}
                    </option>
                  ))}
                </select>
              </>
            )}
          </div>

          {error && <p className="error">{error}</p>}
          {loading && !preview && <p>Carregando…</p>}

          {preview && (
            <div data-cy="accounting-preview">
              <p>
                <span data-cy="accounting-lines">{preview.lines}</span> lançamento(s) e{' '}
                <span data-cy="accounting-payments">{preview.payments}</span> pagamento(s) de fatura em{' '}
                {preview.accounts} conta(s) PJ.
              </p>
              {preview.lastExport &&
                (preview.lastExport.changed ? (
                  <p className="notice warning" data-cy="accounting-changed">
                    Mudou desde a exportação de {when(preview.lastExport.exportedAtUtc)}: eram{' '}
                    {preview.lastExport.lines} lançamento(s) e {preview.lastExport.payments} pagamento(s)
                    {preview.lastExport.totals.map((t) => `, saídas de ${money(t.expense, t.currency)}`).join('')}. Baixe
                    de novo e reenvie ao contador.
                  </p>
                ) : (
                  <p className="muted" data-cy="accounting-unchanged">
                    Exportado em {when(preview.lastExport.exportedAtUtc)}, sem mudanças desde então.
                  </p>
                ))}
              {preview.unmappedLines > 0 && (
                <p className="notice" data-cy="accounting-unmapped">
                  {preview.unmappedLines} lançamento(s) sem conta contábil: a coluna sai vazia. Associe as categorias no
                  plano de contas abaixo, se o contador pedir.
                </p>
              )}
              {preview.totals.length > 0 && (
                <div className="table-scroll">
                  <table className="data" data-cy="accounting-totals">
                    <thead>
                      <tr>
                        <th>Moeda</th>
                        <th className="right">Entradas</th>
                        <th className="right">Saídas</th>
                        <th className="right">Saldo</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.totals.map((t) => (
                        <tr key={t.currency} data-cy="accounting-total" data-currency={t.currency}>
                          <td>{t.currency}</td>
                          <td className="right" data-cy="accounting-income">{money(t.income, t.currency)}</td>
                          <td className="right" data-cy="accounting-expense">{money(t.expense, t.currency)}</td>
                          <td className={`right ${t.balance < 0 ? 'negative' : t.balance > 0 ? 'positive' : ''}`}>
                            {money(t.balance, t.currency)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <button className="primary" data-cy="accounting-download" disabled={downloading || loading} onClick={() => void download()}>
                {downloading ? 'Gerando…' : `Baixar ${preview.fileName}`}
              </button>
            </div>
          )}

          <LedgerAccountsSection onChanged={() => setRefreshKey((k) => k + 1)} />
        </>
      )}
    </section>
  );
}
