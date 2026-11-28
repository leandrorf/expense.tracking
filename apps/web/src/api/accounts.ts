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

  /** INV-13: saldo da conta no fim do dia `date` (`yyyy-MM-dd`, hoje ou antes). */
  setOpeningBalance: (id: string, amount: number, date: string) =>
    apiFetch<Account>(`/api/accounts/${id}/opening-balance`, { method: 'PUT', body: JSON.stringify({ amount, date }) }),

  clearOpeningBalance: (id: string) =>
    apiFetch<Account>(`/api/accounts/${id}/opening-balance`, { method: 'DELETE' }),

  listCards: (accountId?: string) =>
    apiFetch<CreditCard[]>(`/api/credit-cards${accountId ? `?accountId=${accountId}` : ''}`),

  createCard: (input: CreateCreditCardInput) =>
    apiFetch<CreditCard>('/api/credit-cards', { method: 'POST', body: JSON.stringify(input) }),

  removeCard: (id: string) => apiFetch<void>(`/api/credit-cards/${id}`, { method: 'DELETE' }),

  /** CYC-2: corrige nome, bandeira, final e limite (a conta e o ciclo não mudam). */
  editCard: (id: string, input: { label: string; brand: string; lastFourDigits: string; creditLimit: number }) =>
    apiFetch<CreditCard>(`/api/credit-cards/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  /** CYC-3: troca o ciclo a partir da fatura que vence em `fromDueMonth` (`yyyy-MM`). */
  changeCardCycle: (id: string, input: { closingDay: number; dueDay: number; fromDueMonth: string }) =>
    apiFetch<CreditCard>(`/api/credit-cards/${id}/cycle`, { method: 'PUT', body: JSON.stringify(input) }),

  /** CYC-3: desfaz uma troca de ciclo que ainda não começou. */
  undoCardCycle: (id: string, fromDueMonth: string) =>
    apiFetch<CreditCard>(`/api/credit-cards/${id}/cycle/${fromDueMonth}`, { method: 'DELETE' }),

  paymentMethods: () => apiFetch<PaymentMethod[]>('/api/payment-methods'),
};
