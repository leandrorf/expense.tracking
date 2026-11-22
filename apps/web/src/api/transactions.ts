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
  /** INV-5: 1 a 24, só no cartão de crédito. */
  installments?: number;
}

export interface TransactionFilters {
  accountId?: string;
  category?: string;
  from?: string;
  to?: string;
}

/** INV-6: pagamento de fatura; nunca vira lançamento. */
export interface RegisterCardPaymentInput {
  creditCardId: string;
  /** Mês de vencimento da fatura paga, `yyyy-MM`. */
  invoiceDueMonth: string;
  accountId: string;
  amount: number;
  paidOn: string;
}

export interface CardPayment extends RegisterCardPaymentInput {
  id: string;
  currency: string;
  createdAtUtc: string;
}

export const cardPaymentsApi = {
  register: (input: RegisterCardPaymentInput) =>
    apiFetch<CardPayment>('/api/transactions/card-payments', { method: 'POST', body: JSON.stringify(input) }),

  remove: (id: string) => apiFetch<void>(`/api/transactions/card-payments/${id}`, { method: 'DELETE' }),
};

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

  get: (id: string) => apiFetch<Transaction>(`/api/transactions/${id}`),

  /** LNC-5 (ADR-0007): o lançamento inteiro mais a versão lida; 409 se outra aba salvou antes. */
  update: (id: string, input: CreateTransactionInput & { version: number }) =>
    apiFetch<Transaction>(`/api/transactions/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  remove: (id: string) => apiFetch<void>(`/api/transactions/${id}`, { method: 'DELETE' }),
};
