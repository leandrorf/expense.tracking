import { apiFetch } from './client';

export interface TotalByKey {
  key: string;
  total: number;
}

export interface ReportSummary {
  currency: string;
  totalExpense: number;
  totalIncome: number;
  net: number;
  count: number;
  byCategory: TotalByKey[];
  byScope: TotalByKey[];
  byPaymentMethod: TotalByKey[];
}

export interface SummaryFilters {
  accountId?: string;
  from?: string;
  to?: string;
}

export const reportsApi = {
  summary: (filters: SummaryFilters = {}) => {
    const params = new URLSearchParams();
    if (filters.accountId) params.set('accountId', filters.accountId);
    if (filters.from) params.set('from', filters.from);
    if (filters.to) params.set('to', filters.to);
    const query = params.toString();
    return apiFetch<ReportSummary>(`/api/reports/summary${query ? `?${query}` : ''}`);
  },
};
