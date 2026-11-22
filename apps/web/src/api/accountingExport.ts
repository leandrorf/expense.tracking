import { apiDownload, apiFetch } from './client';

/** CTB-4: totais de uma moeda, iguais à linha "Total" do resumo.csv. */
export interface AccountingTotals {
  currency: string;
  income: number;
  expense: number;
  balance: number;
}

/** CTB-4: o que o ZIP vai ter, sem gerá-lo. */
export interface AccountingExportPreview {
  fileName: string;
  accounts: number;
  lines: number;
  payments: number;
  totals: AccountingTotals[];
  /** CTB-5: lançamentos cuja categoria não tem conta contábil. */
  unmappedLines: number;
}

/** CTB-5: categoria usada em conta PJ (ou já associada) e a conta contábil dela. */
export interface LedgerAccount {
  category: string;
  code: string | null;
  name: string | null;
  /** Lançamentos PJ com a categoria, em qualquer mês. */
  lines: number;
}

function query(month: string, accountId?: string): string {
  const params = new URLSearchParams({ month });
  if (accountId) params.set('accountId', accountId);
  return params.toString();
}

/** CTB-2 a CTB-4 (ADR-0006): exportação contábil PJ do mês. */
export const accountingExportApi = {
  preview: (month: string, accountId?: string) =>
    apiFetch<AccountingExportPreview>(`/api/transactions/accounting-export/preview?${query(month, accountId)}`),
  download: (month: string, accountId?: string) =>
    apiDownload(`/api/transactions/accounting-export?${query(month, accountId)}`),
};

/** CTB-5 (ADR-0006, decisão 7): plano de contas, categoria → conta contábil. */
export const ledgerAccountsApi = {
  list: () => apiFetch<LedgerAccount[]>('/api/transactions/ledger-accounts'),
  set: (category: string, code: string, name?: string) =>
    apiFetch<LedgerAccount>('/api/transactions/ledger-accounts', {
      method: 'PUT',
      body: JSON.stringify({ category, code, name: name || null }),
    }),
  remove: (category: string) =>
    apiFetch<void>(`/api/transactions/ledger-accounts?${new URLSearchParams({ category })}`, { method: 'DELETE' }),
};
