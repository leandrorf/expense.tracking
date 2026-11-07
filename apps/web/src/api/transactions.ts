import { apiFetch } from './client';
import type { Transaction } from './types';

export interface CreateTransactionInput {
  accountId: string;
  type: 'Expense' | 'Income';
  paymentMethod: string;
  creditCardId?: string;
  description: string;
  category: string;
  amount: number;
  currency?: string;
  occurredOn: string;
  notes?: string;
}

export interface TransactionFilters {
  accountId?: string;
  category?: string;
  from?: string;
  to?: string;
}

export const transactionsApi = {
  list: (filters: TransactionFilters = {}) => {
    const params = new URLSearchParams();
    if (filters.accountId) params.set('accountId', filters.accountId);
    if (filters.category) params.set('category', filters.category);
    if (filters.from) params.set('from', filters.from);
    if (filters.to) params.set('to', filters.to);
    const query = params.toString();
    return apiFetch<Transaction[]>(`/api/transactions${query ? `?${query}` : ''}`);
  },

  create: (input: CreateTransactionInput) =>
    apiFetch<Transaction>('/api/transactions', { method: 'POST', body: JSON.stringify(input) }),

  remove: (id: string) => apiFetch<void>(`/api/transactions/${id}`, { method: 'DELETE' }),
};
