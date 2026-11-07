import { apiFetch } from './client';
import type { Account, CreditCard, PaymentMethod } from './types';

export interface CreateAccountInput {
  name: string;
  scope: 'Personal' | 'Business';
  taxId?: string;
  currency?: string;
}

export interface CreateCreditCardInput {
  accountId: string;
  label: string;
  brand?: string;
  lastFourDigits: string;
  creditLimit: number;
  closingDay: number;
  dueDay: number;
}

export const accountsApi = {
  list: () => apiFetch<Account[]>('/api/accounts'),

  create: (input: CreateAccountInput) =>
    apiFetch<Account>('/api/accounts', { method: 'POST', body: JSON.stringify(input) }),

  remove: (id: string) => apiFetch<void>(`/api/accounts/${id}`, { method: 'DELETE' }),

  listCards: (accountId?: string) =>
    apiFetch<CreditCard[]>(`/api/credit-cards${accountId ? `?accountId=${accountId}` : ''}`),

  createCard: (input: CreateCreditCardInput) =>
    apiFetch<CreditCard>('/api/credit-cards', { method: 'POST', body: JSON.stringify(input) }),

  removeCard: (id: string) => apiFetch<void>(`/api/credit-cards/${id}`, { method: 'DELETE' }),

  paymentMethods: () => apiFetch<PaymentMethod[]>('/api/payment-methods'),
};
