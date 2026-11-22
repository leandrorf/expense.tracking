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
