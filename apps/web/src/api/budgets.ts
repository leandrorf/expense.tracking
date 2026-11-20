import { apiFetch } from './client';

export type BudgetScope = 'Personal' | 'Business';

export interface BudgetLimit {
  /** Mês (`yyyy-MM`) a partir do qual o valor vale. */
  fromMonth: string;
  amount: number;
}

export interface Budget {
  id: string;
  category: string;
  scope: BudgetScope;
  /** Valor da vigência mais recente (ou do mês consultado, com `?month=`). */
  amount: number;
  currency: string;
  startMonth: string;
  /** Mês a partir do qual o orçamento não vale mais; `null` se aberto. */
  closedFromMonth: string | null;
  limits: BudgetLimit[];
}

export interface CreateBudgetInput {
  category: string;
  scope: BudgetScope;
  amount: number;
  startMonth: string;
  currency?: string;
}

export const budgetsApi = {
  list: () => apiFetch<Budget[]>('/api/budgets'),

  create: (input: CreateBudgetInput) =>
    apiFetch<Budget>('/api/budgets', { method: 'POST', body: JSON.stringify(input) }),

  changeAmount: (id: string, amount: number, fromMonth: string) =>
    apiFetch<Budget>(`/api/budgets/${id}/amount`, { method: 'PUT', body: JSON.stringify({ amount, fromMonth }) }),

  close: (id: string, fromMonth: string) =>
    apiFetch<Budget>(`/api/budgets/${id}/close`, { method: 'POST', body: JSON.stringify({ fromMonth }) }),
};
